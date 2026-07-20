# Authentication API Implementation with OpenIddict

## Context
We are implementing the `Authentication.API` microservice using **OpenIddict** and **ASP.NET Core Identity**. This service will handle user registration, login, and token issuance (JWT) for the Tandem application.

## Approach
1. **Update `Authentication.API.csproj`**: Add OpenIddict and Identity packages.
2. **Create Identity Entities**: `ApplicationUser` and `AuthDbContext`.
3. **Configure OpenIddict**: Setup clients, APIs, and token generation in `Program.cs`.
4. **Add Controllers**: `AuthController` for custom endpoints (Register, Refresh).
5. **Add Views**: Login and Register Razor Pages.

## Files to Modify/Create
- `backend/Services/Authentication/Authentication.API/Authentication.API.csproj`
- `backend/Services/Authentication/Authentication.API/Identity/ApplicationUser.cs`
- `backend/Services/Authentication/Authentication.API/Identity/Data/AuthDbContext.cs`
- `backend/Services/Authentication/Authentication.API/Program.cs`
- `backend/Services/Authentication/Authentication.API/Controllers/AuthController.cs`
- `backend/Services/Authentication/Authentication.API/Views/Account/Login.cshtml`
- `backend/Services/Authentication/Authentication.API/Views/Account/Register.cshtml`

## Steps
- [ ] Update `Authentication.API.csproj` with OpenIddict and Identity packages.
- [ ] Create `Identity/ApplicationUser.cs`.
- [ ] Create `Identity/Data/AuthDbContext.cs`.
- [ ] Configure OpenIddict in `Program.cs`.
- [ ] Implement `AuthController.cs`.
- [ ] Create Login and Register views.
