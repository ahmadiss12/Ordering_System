# Web clients

Angular workspace — arrives in **Phase 2**.

```
projects/
  storefront/          customer web app
  dashboard/           restaurant staff + platform admin, routes guarded by role
  shared/api-client/   generated from the API's OpenAPI document
  shared/auth/         token storage, refresh interceptor, route guards
  shared/ui/           Material theme and shared components
```

See `docs/ARCHITECTURE.md` ADR-16 for why one workspace with two apps rather than
two standalone projects or Nx.
