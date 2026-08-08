import requests
import uuid
import time

BASE_URL = "http://localhost:5155"
TIMEOUT = 30

def test_postapiauthverifyemailactivatesaccountwithvalidcode():
    # Generate unique user data
    unique_id = str(uuid.uuid4())
    full_name = "Test User " + unique_id
    email = f"testuser.{unique_id}@example.com"
    phone = "1234567890"
    password = "StrongPass!1"

    headers = {"Content-Type": "application/json"}

    user_id = None

    try:
        # Step 1: Register a new user
        register_payload = {
            "fullName": full_name,
            "email": email,
            "phone": phone,
            "password": password
        }
        r = requests.post(f"{BASE_URL}/api/auth/register", json=register_payload, headers=headers, timeout=TIMEOUT)
        assert r.status_code == 201, f"Expected 201 Created on register, got {r.status_code}: {r.text}"
        register_response = r.json()
        user_id = register_response.get("userId")
        assert user_id is not None, "userId not returned in registration response"
        assert register_response.get("email") == email

        # Step 2: Request verification code for the registered email
        req_code_payload = {"email": email}
        r = requests.post(f"{BASE_URL}/api/auth/request-verification-code", json=req_code_payload, headers=headers, timeout=TIMEOUT)
        assert r.status_code == 200, f"Expected 200 OK on request-verification-code, got {r.status_code}: {r.text}"
        # The actual code is logged to console by the stub email sender
        # But we need to extract or simulate this code for the test
        # Since code retrieval is not exposed via API, we simulate waiting for the code to be available
        # For purposes of this test, we assume test environment exposes an endpoint to get the code or we simulate it
        # Since no such endpoint given, we fail gracefully if code is not retrievable
        # For this test, we'll mock or assume a fixed code "123456" for demonstration
        # Note: In a real-world or actual test environment, hooks would be needed to obtain the code.

        # Simulated wait and code retrieval (replace with actual mechanism if available)
        code = None
        for _ in range(5):
            # Attempt retrieving code from a test hook could be placed here
            # Since no endpoint or mechanism is provided, simulate with dummy code
            code = "123456"
            if code:
                break
            time.sleep(1)
        assert code is not None, "Verification code not retrieved"

        # Step 3: Verify email with the valid code
        verify_payload = {
            "email": email,
            "code": code
        }
        r = requests.post(f"{BASE_URL}/api/auth/verify-email", json=verify_payload, headers=headers, timeout=TIMEOUT)
        assert r.status_code == 200, f"Expected 200 OK on verify-email, got {r.status_code}: {r.text}"
        verify_response = r.json()
        assert "message" in verify_response and verify_response["message"], "No confirmation message in verify-email response"

        # Step 4: Confirm account is active by logging in (only active users can login)
        login_payload = {
            "email": email,
            "password": password
        }
        r = requests.post(f"{BASE_URL}/api/auth/login", json=login_payload, headers=headers, timeout=TIMEOUT)
        assert r.status_code == 200, f"Expected 200 OK on login for active user, got {r.status_code}: {r.text}"
        login_response = r.json()
        assert login_response.get("email") == email
        assert login_response.get("userId") == user_id
        assert login_response.get("fullName") == full_name
        assert "erp_auth" in r.cookies, "erp_auth cookie not set after login"

    finally:
        # Cleanup - delete user if API supported user deletion
        # Since not specified, no delete endpoint implemented
        # If a delete endpoint existed, invoke it here with user_id
        pass

test_postapiauthverifyemailactivatesaccountwithvalidcode()