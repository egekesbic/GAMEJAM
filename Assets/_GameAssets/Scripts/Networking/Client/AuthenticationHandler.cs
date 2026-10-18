using Cysharp.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

public static class AuthenticationHandler 
{
    public static AuthenticationState AuthenticationState
    {
        get;
        private set;
    } = AuthenticationState.NotAuthenticated;

    public static async UniTask<AuthenticationState> DoAuth(int MaxTries = 5)
    {
        if (AuthenticationState == AuthenticationState.Authenticated)
        {
            return AuthenticationState;
        }

        if (AuthenticationState == AuthenticationState.Authenticating)
        {
            await Authenticating();
            return AuthenticationState;
        }

        await SignAnonymouslyAsync(MaxTries);

        return AuthenticationState;
    }

    private static async UniTask<AuthenticationState> Authenticating()
    {
        while (AuthenticationState == AuthenticationState.Authenticating
            || AuthenticationState == AuthenticationState.NotAuthenticated)
        {
            await UniTask.Delay(200);
        }

        return AuthenticationState;
    }

    private static async UniTask SignAnonymouslyAsync(int maxTries)
    {
        AuthenticationState = AuthenticationState.Authenticating;

        int attemptCount = Mathf.Max(1, maxTries);
        for (int attempt = 1; attempt <= attemptCount; attempt++)
        {
            try
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

                if (AuthenticationService.Instance.IsSignedIn && AuthenticationService.Instance.IsAuthorized)
                {
                    AuthenticationState = AuthenticationState.Authenticated;
                    return;
                }
            }
            catch (AuthenticationException authenticationException)
            {
                Debug.LogError(authenticationException);
            }
            catch (RequestFailedException requestFailedException)
            {
                Debug.LogError(requestFailedException);
            }

            if (attempt < attemptCount)
            {
                Debug.LogWarning($"Anonymous sign-in attempt {attempt} of {attemptCount} failed; retrying.");
                await UniTask.Delay(1000);
            }
        }

        AuthenticationState = AuthenticationState.Timeout;
        Debug.LogError($"Anonymous sign-in failed after {attemptCount} attempt(s).");
    }
}
