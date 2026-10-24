// Preserve the task that required authentication. The login page/server still
// validate this hint; it never grants access to the destination.
export function loginRedirectUrl(location) {
    const path = location?.pathname;
    if (typeof path !== "string" || !path.startsWith("/") || path.startsWith("//") || path.includes("\\"))
        return "/login";
    const destination = path + (location.search ?? "") + (location.hash ?? "");
    return "/login?ReturnUrl=" + encodeURIComponent(destination);
}
