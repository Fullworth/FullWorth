using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Statements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Controllers;

[ApiController]
[Authorize]
[Route("api/bill-streams/{billStreamId:guid}/statement-uploads")]
public sealed class BillStatementUploadsController
    : ControllerBase
{
    private const long MaxMultipartBodyLength =
        16L * 1024 * 1024;

    private readonly FullWorthDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SecureBillStatementStorageService _storageService;
    private readonly BillStatementProcessingSignal _processingSignal;
    private readonly IBillStreamReadGateway _billStreamGateway;

    public BillStatementUploadsController(
        FullWorthDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        SecureBillStatementStorageService storageService,
        BillStatementProcessingSignal processingSignal,
        IBillStreamReadGateway billStreamGateway)
    {
        _dbContext =
            dbContext;

        _userManager =
            userManager;

        _storageService =
            storageService;

        _processingSignal =
            processingSignal;

        _billStreamGateway =
            billStreamGateway;
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxMultipartBodyLength)]
    [RequestFormLimits(
        MultipartBodyLengthLimit =
            MaxMultipartBodyLength)]
    [EnableRateLimiting("statement-upload")]
    public async Task<ActionResult<BillStatementUploadResult>>
        UploadStatement(
            Guid billStreamId,
            [FromForm] IFormFile? file,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(
                out var userId))
        {
            return Unauthorized();
        }

        if (billStreamId ==
            Guid.Empty)
        {
            return NotFound();
        }

        var billStream =
            await _billStreamGateway.GetOwnedAsync(
                userId,
                billStreamId,
                cancellationToken);

        if (billStream is null)
        {
            return NotFound();
        }

        if (file is null)
        {
            return BadRequest(
                new
                {
                    message =
                        "Select a bill statement to upload."
                });
        }

        if (file.Length <= 0)
        {
            return BadRequest(
                new
                {
                    message =
                        "The uploaded bill is empty."
                });
        }

        StoredBillStatementFile storedFile;

        try
        {
            await using var uploadStream =
                file.OpenReadStream();

            storedFile =
                await _storageService.StoreAsync(
                    userId,
                    uploadStream,
                    file.FileName,
                    cancellationToken);
        }
        catch (BillStatementFileValidationException ex)
        {
            return BadRequest(
                new
                {
                    message =
                        ex.Message
                });
        }

        var now =
            DateTimeOffset.UtcNow;

        var upload =
            new BillStatementUploadEntity
            {
                UserId =
                    userId,

                BillStreamId =
                    billStreamId,

                StorageKey =
                    storedFile.StorageKey,

                MediaType =
                    storedFile.MediaType,

                FileExtension =
                    storedFile.FileExtension,

                SizeBytes =
                    storedFile.SizeBytes,

                Status =
                    BillStatementUploadStatus.Uploaded,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };

        try
        {
            _dbContext.BillStatementUploads.Add(
                upload);

            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch
        {
            _storageService.Delete(
                storedFile.StorageKey);

            throw;
        }

        _processingSignal.Notify();

        return Created(
            $"/api/bill-streams/{billStreamId}/statement-uploads/{upload.Id}",
            new BillStatementUploadResult(
                Id:
                    upload.Id,

                BillStreamId:
                    upload.BillStreamId,

                MediaType:
                    upload.MediaType,

                FileExtension:
                    upload.FileExtension,

                SizeBytes:
                    upload.SizeBytes,

                Status:
                    upload.Status.ToString(),

                CreatedAtUtc:
                    upload.CreatedAtUtc));
    }

    private bool TryGetUserId(
        out Guid userId)
    {
        var userIdText =
            _userManager.GetUserId(
                User);

        return Guid.TryParse(
            userIdText,
            out userId);
    }
}

public sealed record BillStatementUploadResult(
    Guid Id,
    Guid BillStreamId,
    string MediaType,
    string FileExtension,
    long SizeBytes,
    string Status,
    DateTimeOffset CreatedAtUtc);