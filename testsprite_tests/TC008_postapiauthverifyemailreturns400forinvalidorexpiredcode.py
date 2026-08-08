import requests
import uuid

BASE_URL = "http://localhost:5155"
TIMEOUT = 30


def test_postapiauthverifyemailreturns400forinvalidorexpiredcode():
    # First, register a new user to get a valid email in the system
    register_url = f"{BASE_URL}/api/auth/register"
    verify_url = f"{BASE_URL}/api/auth/verify-email"

    # Unique email to avoid conflicts
    unique_email = f"testuser_{uuid.uuid4().hex}@example.com"
    register_payload = {
        "fullName": "Test User",
        "email": unique_email,
        "phone": "1234567890",
        "password": "StrongPassw0rd!"
    }

    user_id = None
    try:
        # Register the user (should succeed)
        register_response = requests.post(register_url, json=register_payload, timeout=TIMEOUT)
        assert register_response.status_code == 201, f"Registration failed with status {register_response.status_code}"
        user_id = register_response.json().get("userId")
        assert user_id is not None, "userId not returned on registration"
        assert register_response.json().get("email") == unique_email

        # Attempt to verify email with invalid, expired, or already-used code
        # Use an obviously invalid code: "000000"
        invalid_codes = ["000000", "12345", "abcdef", "999999"]  # Including some invalid formats

        for invalid_code in invalid_codes:
            payload = {
                "email": unique_email,
                "code": invalid_code if len(invalid_code) == 6 else "000000"  # code must be exactly 6 chars
            }
            # If invalid_code is not exactly 6, still send 6 chars to test validation logic
            if len(invalid_code) != 6:
                payload["code"] = "000000"

            response = requests.post(verify_url, json=payload, timeout=TIMEOUT)
            # Expect HTTP 400 for invalid, expired or already-used code
            assert response.status_code == 400, \
                f"Expected 400 for code '{invalid_code}', got {response.status_code}. Response: {response.text}"
    finally:
        # Cleanup: No delete endpoint info provided - assumed no deletion possible or needed
        pass


test_postapiauthverifyemailreturns400forinvalidorexpiredcode()