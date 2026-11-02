# Experimental UWP HTTPS GET bridge

The optional `uwp-https` feature routes skin/profile texture GETs through
Windows.Web.Http on a background worker. The host must also be built with
`HORIZON_UWP_HTTP=1`, which compiles and links the native bridge. Builds without
the feature and non-UWP targets retain their existing networking code.

The bridge uses the system certificate store and default validation without
adding ignored certificate errors. Redirects are limited to five, and a request
that started as HTTPS cannot redirect to HTTP. Response bodies are streamed with
a 16 MiB upper bound and network operations time out after 15 seconds. Only
successful 2xx responses are returned. This does not yet route account/session
HTTP calls, telemetry or server resource packs through the bridge, and does not
implement Microsoft sign-in.

A Windows UWP x64 harness has tested a successful real Mojang HTTPS GET, failure
for a deliberately expired certificate, and failure when the body exceeds its
limit. The bridge has compiled with the ARM32 15063 SDK, but the complete game
path and Lumia TLS behavior still need device validation.
