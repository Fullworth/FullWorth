using FullWorth.API.Authorization;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Accounts;
using FullWorth.API.Services.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FullWorth.API.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
public sealed class AccountController : ControllerBase
{
    private readonly FullWorthDbContext _dbContext;
    private readonly AccountDataExportBuilder _accountDataExportBuilder;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAccountBankDeletionGateway _bankDeletionGateway;
    private readonly IAccountBillDeletionGateway _billDeletionGateway;
    private readonly IAccountStatementDeletionGateway _statementDeletionGateway;
    private readonly IAccountSubscriptionDeletionGateway _subscriptionDeletionGateway;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        FullWorthDbContext dbContext,
        AccountDataExportBuilder accountDataExportBuilder,
        UserManager<ApplicationUser> userManager,
        IAccountBankDeletionGateway bankDeletionGateway,
        IAccountBillDeletionGateway billDeletionGateway,
        IAccountStatementDeletionGateway statementDeletionGateway,
        IAccountSubscriptionDeletionGateway subscriptionDeletionGateway,
        ILogger<AccountController> logger)
    {
        _dbContext = dbContext;
        _accountDataExportBuilder = accountDataExportBuilder;
        _userManager = userManager;
        _bankDeletionGateway = bankDeletionGateway;
        _billDeletionGateway = billDeletionGateway;
        _statementDeletionGateway = statementDeletionGateway;
        _subscriptionDeletionGateway = subscriptionDeletionGateway;
        _logger = logger;
    }

    [HttpPost("export")]
    [EnableRateLimiting("account-export")]
    public async Task<ActionResult<AccountDataExportResult>> ExportAccountData(
        AccountDataExportRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
            !await _userManager.CheckPasswordAsync(
                user,
                request.CurrentPassword))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Current password is incorrect.");
        }

        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "A current authenticator code is required.");
            }

            var validTwoFactorCode =
                await _userManager.VerifyTwoFactorTokenAsync(
                    user,
                    _userManager.Options.Tokens.AuthenticatorTokenProvider,
                    NormalizeAuthenticatorCode(request.TwoFactorCode));

            if (!validTwoFactorCode)
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "The authenticator code is invalid.");
            }
        }

        var export = await _accountDataExportBuilder.CreateAsync(
            user,
            cancellationToken);

        Response.Headers.Append(
            "Content-Disposition",
            "attachment; filename=\"fullworth-data-export.json\"");

        return Ok(export);
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAccount(
        DeleteAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return NoContent();
        }

        if (!string.Equals(
                request.Confirmation,
                "DELETE",
                StringComparison.Ordinal))
        {
            return BadRequest(
                new
                {
                    message =
                        "Type DELETE to confirm permanent account deletion."
                });
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
            !await _userManager.CheckPasswordAsync(
                user,
                request.CurrentPassword))
        {
            return UnauthorizedDeletion(
                "Current password is incorrect.");
        }

        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
            {
                return UnauthorizedDeletion(
                    "A current authenticator code is required.");
            }

            var validTwoFactorCode =
                await _userManager.VerifyTwoFactorTokenAsync(
                    user,
                    _userManager.Options.Tokens.AuthenticatorTokenProvider,
                    NormalizeAuthenticatorCode(request.TwoFactorCode));

            if (!validTwoFactorCode)
            {
                return UnauthorizedDeletion(
                    "The authenticator code is invalid.");
            }
        }

        /*
         * Staff identities anchor privileged audit/access-key history.
         * Do not silently erase that security provenance or relax its
         * restrictive foreign keys. A privileged operator must remove all
         * staff roles first, after which normal self-service deletion can
         * proceed through this same path.
         */
        var roles = await _userManager.GetRolesAsync(user);

        if (roles.Any(FullWorthRoles.IsStaffRole))
        {
            return Conflict(
                new
                {
                    message =
                        "FullWorth staff accounts cannot be self-deleted while privileged roles are assigned. Remove the staff roles through the authorized admin workflow first."
                });
        }

        /*
         * External provider access is revoked by the Plaid-owned boundary
         * before local financial records enter the deletion transaction.
         */
        try
        {
            await _bankDeletionGateway
                .RevokeExternalAccessAsync(
                    userId,
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AccountBankRevocationException exception)
        {
            _logger.LogWarning(
                "FullWorth account deletion could not revoke a bank connection because of {ExceptionType}.",
                exception.ExceptionType);

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    message =
                        "FullWorth could not safely finish deleting your account because a bank connection could not be revoked. Your FullWorth account was not deleted. Try again shortly."
                });
        }

        /*
         * The filesystem cannot participate in the PostgreSQL transaction.
         * Move owned statements into a same-volume quarantine first instead
         * of destroying them. A DB rollback restores them. A committed user
         * deletion purges them. The statement worker reconciles an interrupted
         * quarantine after process crashes by checking whether the user still
         * exists.
         */
        IReadOnlyList<AccountStatementQuarantineEntry>
            quarantinedStatements;

        try
        {
            quarantinedStatements =
                await _statementDeletionGateway
                    .QuarantineOwnedFilesAsync(
                        userId,
                        cancellationToken);
        }
        catch (Exception exception)
            when (exception is
                IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                ArgumentException)
        {
            _logger.LogError(
                "FullWorth account deletion could not quarantine statement storage because of {ExceptionType}.",
                exception.GetType().Name);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    message =
                        "FullWorth could not securely prepare stored statement files for deletion. Your FullWorth account was not deleted. Try again."
                });
        }

        IDbContextTransaction? transaction = null;
        var databaseCommitted = false;

        try
        {
            if (_dbContext.Database.IsRelational())
            {
                transaction = await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken);
            }

            /*
             * Each owning module erases its own rows through a narrow
             * account-deletion contract. Every gateway shares this scoped
             * DbContext, so its SaveChanges calls remain inside the same
             * relational transaction.
             *
             * Order preserves the existing restrictive foreign keys:
             * subscriptions first; Bills alerts before Statement changes;
             * Statements before bank/root resources; Bill Streams last.
             */
            await _subscriptionDeletionGateway
                .ApplyOwnedDataDeletionAsync(
                    userId,
                    cancellationToken);

            await _billDeletionGateway
                .ApplyDependentDataDeletionAsync(
                    userId,
                    cancellationToken);

            await _statementDeletionGateway
                .ApplyOwnedDataDeletionAsync(
                    userId,
                    cancellationToken);

            await _bankDeletionGateway
                .ApplyOwnedDataDeletionAsync(
                    userId,
                    cancellationToken);

            await _billDeletionGateway
                .ApplyRootDataDeletionAsync(
                    userId,
                    cancellationToken);

            /*
             * Identity data is last. Admin audit targets and grantor
             * references are configured SetNull, so deleting a normal user
             * cannot erase unrelated security provenance.
             */
            var identityResult = await _userManager.DeleteAsync(user);

            if (!identityResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Identity account deletion failed.");
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            databaseCommitted = true;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            RestoreQuarantinedStatementsBestEffort(
                quarantinedStatements);

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        if (!databaseCommitted)
        {
            throw new InvalidOperationException(
                "Account deletion did not reach a committed state.");
        }

        var cleanupPending = false;

        foreach (var entry in quarantinedStatements)
        {
            try
            {
                _statementDeletionGateway.CommitQuarantine(
                    entry);
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    InvalidOperationException or
                    ArgumentException)
            {
                cleanupPending = true;

                _logger.LogError(
                    "FullWorth account deletion committed, but quarantined statement cleanup is pending because of {ExceptionType}.",
                    exception.GetType().Name);
            }
        }

        if (cleanupPending)
        {
            return Accepted(
                value: new
                {
                    message =
                        "Your FullWorth account was deleted. Secure cleanup of quarantined statement files is still being retried automatically."
                });
        }

        return NoContent();
    }

    private ObjectResult UnauthorizedDeletion(
        string title)
    {
        return Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: title);
    }

    private void RestoreQuarantinedStatementsBestEffort(
        IEnumerable<AccountStatementQuarantineEntry> entries)
    {
        foreach (var entry in entries.Reverse())
        {
            try
            {
                _statementDeletionGateway.RestoreQuarantine(
                    entry);
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    InvalidOperationException or
                    ArgumentException)
            {
                _logger.LogCritical(
                    "FullWorth could not immediately restore a quarantined statement after account deletion rollback because of {ExceptionType}. Startup maintenance will retry recovery.",
                    exception.GetType().Name);
            }
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        var userIdText = _userManager.GetUserId(User);
        return Guid.TryParse(userIdText, out userId);
    }

    private static string NormalizeAuthenticatorCode(
        string code)
    {
        return code
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
    }
}

public sealed record AccountDataExportRequest(
    string CurrentPassword,
    string? TwoFactorCode);

public sealed record DeleteAccountRequest(
    string Confirmation,
    string CurrentPassword,
    string? TwoFactorCode);
