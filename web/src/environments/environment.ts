export const environment = {
  production: true,
  apiBaseUrl: '/api',
  // Site key is public by design (baked into the widget's own HTML) -- swap for the real
  // production Turnstile sitekey from the Cloudflare dashboard at deploy time. The matching
  // secret key is server-side only, set via user-secrets/environment, never here.
  turnstileSiteKey: '1x00000000000000000000AA',
};
