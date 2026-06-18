# Version 2G Shibboleth Deployment Checklist

## Server/IIS Boundary

- [ ] Confirm app is hosted behind IIS/Shibboleth.
- [ ] Confirm direct app access is blocked.
- [ ] Confirm spoofed browser headers are stripped.
- [ ] Confirm only trusted server headers reach ASP.NET Core.

## Header Mapping

Confirm actual UVA header names for:

```text
eppn
uid
displayName
mail
affiliation
entitlement
memberOf
```

Update appsettings if header names differ.

## App Config

Set:

```json
"Auth": {
  "Mode": "ShibbolethHeaders",
  "AllowAutoProvisionUsers": true,
  "DefaultRoleForNewUsers": "Reader",
  "RequireEnabledUser": true
}
```

## Role Mapping

- [ ] Confirm Reader group.
- [ ] Confirm Reviewer group.
- [ ] Confirm Admin group.
- [ ] Populate `openalex.AppExternalRoleMapping` if using group mapping in the next hardening pass.
- [ ] Confirm admin user exists before deployment.

## Smoke Test

- [ ] Login as Reader.
- [ ] Login as Reviewer.
- [ ] Login as Admin.
- [ ] Confirm auth events are logged.
- [ ] Confirm unauthorized actions are denied.
