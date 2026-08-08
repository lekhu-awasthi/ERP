import requests
import uuid
import time

BASE_URL = "http://localhost:5155"
TIMEOUT = 30


def test_postapiauthloginauthenticatesverifieduserandsetscookie():
    session = requests.Session()

    # Step 1: Register a new user
    user_email = f"verifieduser_{uuid.uuid4()}@example.com"
    user_password = "StrongP@ssword1"
    user_fullName = "Verified User"
    user_phone = "1234567890"

    register_payload = {
        "fullName": user_fullName,
        "email": user_email,
        "phone": user_phone,
        "password": user_password
    }
    register_resp = session.post(
        f"{BASE_URL}/api/auth/register", json=register_payload, timeout=TIMEOUT
    )
    assert register_resp.status_code == 201, f"Registration failed: {register_resp.text}"
    user_id = register_resp.json().get("userId")
    assert user_id is not None

    try:
        # Step 2: Request verification code
        request_code_payload = {"email": user_email}
        request_code_resp = session.post(
            f"{BASE_URL}/api/auth/request-verification-code",
            json=request_code_payload,
            timeout=TIMEOUT,
        )
        assert request_code_resp.status_code == 200, f"Request verification code failed: {request_code_resp.text}"
        time.sleep(0.5)

        # Step 3: Use a dummy 6-digit code to verify email (since actual code is not returned by API)
        verification_code = "123456"
        verify_email_payload = {"email": user_email, "code": verification_code}
        verify_email_resp = session.post(
            f"{BASE_URL}/api/auth/verify-email",
            json=verify_email_payload,
            timeout=TIMEOUT,
        )
        assert verify_email_resp.status_code == 200, f"Email verification failed: {verify_email_resp.text}"

        # Step 4: Login with verified email and password
        login_payload = {"email": user_email, "password": user_password}
        login_resp = session.post(
            f"{BASE_URL}/api/auth/login", json=login_payload, timeout=TIMEOUT
        )
        assert login_resp.status_code == 200, f"Login failed: {login_resp.text}"
        login_json = login_resp.json()
        assert "userId" in login_json and login_json["userId"] == user_id
        assert login_json.get("email") == user_email
        assert login_json.get("fullName") == user_fullName

        # Check erp_auth cookie set
        erp_auth_cookie = login_resp.cookies.get("erp_auth")
        assert erp_auth_cookie is not None, "erp_auth cookie not set"

    finally:
        pass


test_postapiauthloginauthenticatesverifieduserandsetscookie()
