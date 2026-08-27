export const environment = {
  production: false,
  // Matches src/Api/Properties/launchSettings.json's "https" profile.
  apiBaseUrl: 'https://localhost:7104',
  // Cloudflare's always-passes dummy sitekey (public by design, safe to commit) -- pairs with the
  // matching dummy secret key documented in src/Api/appsettings.Development.json's Turnstile section.
  turnstileSiteKey: '1x00000000000000000000AA',
};
