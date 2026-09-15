package com.crushroyale.signin;

import android.app.Activity;
import android.os.CancellationSignal;

import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialException;

import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;
import com.unity3d.player.UnityPlayer;

import java.util.concurrent.Executors;

/**
 * Sign in with Google through Android Credential Manager and return the ID token to Unity.
 *
 * Gradle dependencies (Player Settings > Publishing Settings > Custom Main Gradle Template):
 *   implementation 'androidx.credentials:credentials:1.3.0'
 *   implementation 'androidx.credentials:credentials-play-services-auth:1.3.0'
 *   implementation 'com.google.android.libraries.identity.googleid:googleid:1.1.1'
 */
public final class GoogleSignInBridge {

    private GoogleSignInBridge() {
    }

    public static void signIn(final Activity activity, final String webClientId, final String hashedNonce, final String callbackObject) {
        GetSignInWithGoogleOption option = new GetSignInWithGoogleOption.Builder(webClientId)
                .setNonce(hashedNonce)
                .build();

        GetCredentialRequest request = new GetCredentialRequest.Builder()
                .addCredentialOption(option)
                .build();

        CredentialManager manager = CredentialManager.create(activity);
        manager.getCredentialAsync(
                activity,
                request,
                new CancellationSignal(),
                Executors.newSingleThreadExecutor(),
                new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                    @Override
                    public void onResult(GetCredentialResponse result) {
                        Credential credential = result.getCredential();
                        if (credential instanceof CustomCredential
                                && GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType())) {
                            try {
                                GoogleIdTokenCredential google = GoogleIdTokenCredential.createFrom(((CustomCredential) credential).getData());
                                UnityPlayer.UnitySendMessage(callbackObject, "OnIdToken", google.getIdToken());
                            } catch (Exception e) {
                                UnityPlayer.UnitySendMessage(callbackObject, "OnError", "invalid_google_credential");
                            }
                        } else {
                            UnityPlayer.UnitySendMessage(callbackObject, "OnError", "unexpected_credential_type");
                        }
                    }

                    @Override
                    public void onError(GetCredentialException e) {
                        UnityPlayer.UnitySendMessage(callbackObject, "OnError", e.getClass().getSimpleName() + ": " + e.getMessage());
                    }
                });
    }
}
