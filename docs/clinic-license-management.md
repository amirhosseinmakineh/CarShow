# Clinic licensing on CarShow

Management: `https://api.mayakhodro.com/admin/licenses`.
Clinic check endpoint: `https://api.mayakhodro.com/api/licenses/check`.

CarShow routes only the management page, its two assets, the allowlisted management
API operations and the signed clinic check to an independent loopback process.
CarShow's vehicle APIs, JWT authorization and database schema are unchanged.
The private Framework source and all runtime keys stay out of this public repo.

## Deployment

1. Mount a persistent disk at `/data/licensing`; use one replica.
2. Publish the private `DentalDashboard-Framework/LicenseServer/Api/Api.csproj`:
   `dotnet publish -c Release -r linux-x64 --self-contained true -p:InvariantGlobalization=true -o /tmp/license-runtime`.
3. Commit changes, then run `python3 scripts/package-clinic-licensing.py /tmp/license-runtime /tmp/carshow-licensing.zip`.
4. Upload the bundle to the existing Runflare Docker service. Keep automatic Git
   deployment off: an ordinary source deployment does not include the private runtime.
5. Verify the CarShow live health endpoint, management login and signed checks.

The bundle enables `ClinicLicensing__Enabled=true`. Optional settings are
`ClinicLicensing__PublicOrigin` (defaults to `https://api.mayakhodro.com`),
`ClinicLicensing__RuntimePath` and `ClinicLicensing__DataPath`.
HTTPS must terminate at the hosting ingress, which supplies X-Forwarded-Proto.
The management gateway requires its exact host and same-origin mutation requests.

The first run creates a signing key and an admin key with owner-only permissions on
the disk. Obtain `/data/licensing/admin-key` from the authenticated host terminal;
it is not logged or exposed by an API. Back up the entire disk. Existing keys must
never be replaced when records exist. The service fails closed when keys are missing.
A failed licensing process does not stop vehicle APIs.

## Clinic connection

Sign in, create a license, and configure the clinic's existing license integration:
`License__ServerUrl=https://api.mayakhodro.com`, installation ID and secret from the
panel, product `dental-clinic`, exact environment, and public key under
`License__PublicKeys__carshow-clinic-1`. Never give the clinic the admin/private key.
The panel's last connection must update before claiming the live clinic is connected.
Disable/re-enable a disposable installation for verification; never disable a real
clinic merely to test the panel. Changes apply on the clinic's next check.

For rollback, redeploy the previous CarShow image and preserve the license disk.
