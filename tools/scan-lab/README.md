# Scan lab query editor

Disabled until ConnectionStrings__ScanLabReadOnly is configured. Never reuse the application connection or sa. A DBA must create a dedicated SQL login with a strong secret, no server roles beyond public, no access to other application databases, and a database user named ScanLabReader. In CarShow, grant only SELECT on dbo.TelegramRawMessages:

```sql
CREATE USER [ScanLabReader] FOR LOGIN [ScanLabReader];
GRANT SELECT ON OBJECT::dbo.TelegramRawMessages TO [ScanLabReader];
DENY INSERT, UPDATE, DELETE, EXECUTE TO [ScanLabReader];
```

Verify effective permissions including public grants, cross-database access, impersonation and external access before enabling. Do not grant additional roles or permissions. Use a separate restricted login, not an impersonated application administrator. Configure its connection string as a secret in deployment. The endpoint checks principal and common elevated privileges, but these checks are not a substitute for DBA provisioning and auditing. SQL execution is limited to 15 seconds and 500 returned rows; cells are capped at 4000 characters. Only the first result set is displayed. Do not enable on an unverified database principal.
