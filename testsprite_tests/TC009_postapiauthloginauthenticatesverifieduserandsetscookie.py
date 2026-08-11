import requests
import uuid
import time

base_url = "http://localhost:5155"

def test_post_api_auth_login_authenticated_verified_user_sets_cookie():
    session = requests.Session()
    session.headers.update({"Content-Type": "application/json"})
    timeout = 30

    # Step 1: Register a new user
    unique_email = f"testuser_{uuid.uuid4().hex}@example.com"
    register_payload = {
        "fullName": "Test User",
        "email": unique_email,
        "phone": "1234567890",
        "password": "Str0ngPassw0rd!"
    }
    register_resp = session.post(f"{base_url}/api/auth/register", json=register_payload, timeout=timeout)
    assert register_resp.status_code == 201, f"Registration failed: {register_resp.text}"
    registered_user_id = register_resp.json().get("userId")
    assert registered_user_id is not None

    try:
        # Step 2: Request verification code for the registered user
        req_verif_payload = {"email": unique_email}
        req_verif_resp = session.post(f"{base_url}/api/auth/request-verification-code", json=req_verif_payload, timeout=timeout)
        assert req_verif_resp.status_code == 200, f"Request verification code failed: {req_verif_resp.text}"

        # NOTE: The verification code is logged by the stub email sender; no real email.
        # Emulate a small delay to ensure code issuance logging completes.
        time.sleep(0.5)

        # Since we don't have real email access, fetch the code from the test system logs or DB would be required;
        # Not possible here, so simulate by extracting from a test helper endpoint or assume code '123456'.
        # To properly test, we request the verification code again and parse from response/logs is needed.
        # Because not possible, use a known valid 6-digit code placeholder to allow test progression.
        verification_code = "123456"

        # Step 3: Verify email with code
        verify_payload = {
            "email": unique_email,
            "code": verification_code
        }
        verify_resp = session.post(f"{base_url}/api/auth/verify-email", json=verify_payload, timeout=timeout)
        if verify_resp.status_code == 400:
            # Possibly invalid or expired code; test cannot continue properly.
            # Raise to fail test, as we cannot proceed to login without verification.
            raise AssertionError(f"Email verification failed with 400 error: {verify_resp.text}")
        elif verify_resp.status_code == 404:
            raise AssertionError(f"Email verification failed with 404 error: {verify_resp.text}")
        else:
            assert verify_resp.status_code == 200, f"Email verification unexpected status: {verify_resp.status_code}"

        # Step 4: Attempt login with verified email and correct password
        login_payload = {
            "email": unique_email,
            "password": "Str0ngPassw0rd!"
        }
        login_resp = session.post(f"{base_url}/api/auth/login", json=login_payload, timeout=timeout)
        assert login_resp.status_code == 200, f"Login failed: {login_resp.text}"

        # Validate response payload: userId, email, fullName
        login_json = login_resp.json()
        assert isinstance(login_json.get("userId"), str) and len(login_json["userId"]) > 0
        assert login_json.get("email") == unique_email
        assert login_json.get("fullName") == "Test User"

        # Validate erp_auth cookie is set with httpOnly attribute
        cookies = login_resp.cookies
        assert "erp_auth" in cookies, "erp_auth cookie not set"

        # Since requests lib does not expose cookie HttpOnly flag, check from response headers:
        set_cookie_headers = login_resp.headers.get("Set-Cookie")
        assert set_cookie_headers is not None, "No Set-Cookie header in login response"
        cookie_lower = set_cookie_headers.lower()
        # Check cookie name, httponly, secure, samesite
        assert "erp_auth=" in cookie_lower
        assert "httponly" in cookie_lower
        assert "secure" in cookie_lower
        # SameSite=None check (may appear as samesite=none)
        assert "samesite=none" in cookie_lower

    finally:
        # Cleanup: There is no direct delete user endpoint mentioned in the PRD, so no deletion is done.
        # If system supports cleanup or user disabling, implement here.
        pass

test_post_api_auth_login_authenticated_verified_user_sets_cookie()