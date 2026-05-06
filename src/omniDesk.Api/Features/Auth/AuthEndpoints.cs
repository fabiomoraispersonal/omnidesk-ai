using omniDesk.Api.Features.Auth.ForgotPassword;
using omniDesk.Api.Features.Auth.Invite;
using omniDesk.Api.Features.Auth.Login;
using omniDesk.Api.Features.Auth.Logout;
using omniDesk.Api.Features.Auth.Refresh;
using omniDesk.Api.Features.Auth.ResetPassword;
using omniDesk.Api.Features.Auth.Sessions;
using omniDesk.Api.Features.Auth.Totp;

namespace omniDesk.Api.Features.Auth;

public static class AuthEndpointRegistration
{
    public static WebApplication MapAuthEndpoints(this WebApplication app, RouteGroupBuilder auth)
    {
        LoginEndpoint.Map(auth);
        RefreshEndpoint.Map(auth);
        LogoutEndpoint.Map(auth);
        ForgotPasswordEndpoint.Map(auth);
        ResetPasswordEndpoint.Map(auth);
        SendInviteEndpoint.Map(auth);
        AcceptInviteEndpoint.Map(auth);
        TotpSetupEndpoint.Map(auth);
        TotpConfirmEndpoint.Map(auth);
        TotpVerifyEndpoint.Map(auth);
        TotpDisableEndpoint.Map(auth);
        ListSessionsEndpoint.Map(auth);
        RevokeSessionEndpoint.Map(auth);
        RevokeAllSessionsEndpoint.Map(auth);
        // T034: POST /reset-password  (US2)
        // T037: POST /invite          (US3)
        // T038: POST /accept-invite   (US3)
        // T042: POST /totp/setup      (US4)
        // T043: POST /totp/confirm    (US4)
        // T044: POST /totp/verify     (US4)
        // T045: DELETE /totp          (US4)
        // T047: GET /sessions         (US5)
        // T048: DELETE /sessions/{id} (US5)
        // T049: DELETE /sessions      (US5)
        return app;
    }
}
