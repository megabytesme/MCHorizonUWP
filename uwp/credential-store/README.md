# UWP Credential Locker adapter

This optional host adapter uses `Windows.Security.Credentials.PasswordVault`
for per-account secrets. It distinguishes a missing key from storage errors,
rejects invalid or oversized input, never logs values and clears its temporary
UTF-8 output allocation before freeing it. The `Secret` wrapper redacts Debug
output; this is not a promise that platform or Rust string copies are wiped.
The application must handle errors without falling back to plaintext storage.

The API is a standalone building block; the game does not yet call it and this
crate does not implement Microsoft sign-in. A UWP app is limited to 20 locker
entries. A synthetic desktop UWP probe exercised missing, storing and reading
an 8,192-character value, replacement, removal and missing again. The ARM32
probe passed SDK 15063 strict linking; the user confirmed an earlier short-value
Credential Locker round trip on Lumia. Long values and this adapter remain to be
tested on the phone.
