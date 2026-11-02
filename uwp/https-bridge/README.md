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

The separate `request` API supports HTTPS GET and bounded POST for future
account flows. It returns HTTP error statuses and bodies, accepts an optional
Authorization header and content type (including form and JSON), and never
follows redirects. It rejects plaintext schemes, CR/LF in headers, request
bodies over 1 MiB and response bodies over the caller's bound. It does not log
request headers or bodies. Call it on a background thread.

`probe/Auth.cpp` exercises live HTTPS JSON and form POST using dummy values,
non-2xx response handling, expiry rejection, plaintext rejection, redirects,
body limits and header injection. A desktop UWP execution passed all these
checks. Its ARM32 executable passed the 15063 strict link audit with the native
request function retained. The account flow and phone runtime remain untested.
