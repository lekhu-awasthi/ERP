import requests

def test_postapiauthverifyemailreturns400forinvalidorexpiredcode():
    base_url = "http://localhost:5155"
    register_url = f"{base_url}/api/auth/register"
    request_code_url = f"{base_url}/api/auth/request-verification-code"
    verify_email_url = f"{base_url}/api/auth/verify-email"

    test_email = "testuser_invalid_code@example.com"
    test_password = "TestPass123!"
    test_full_name = "Test User InvalidCode"
    test_phone = "1234567890"

    # Register new user to ensure the email exists for verification attempts
    register_payload = {
        "fullName": test_full_name,
        "email": test_email,
        "phone": test_phone,
        "password": test_password
    }

    # Create the user then attempt invalid verification
    try:
        reg_resp = requests.post(register_url, json=register_payload, timeout=30)
        assert reg_resp.status_code == 201, f"Expected 201 on registration, got {reg_resp.status_code}"
        user_info = reg_resp.json()
        assert user_info.get("email") == test_email

        # Request a verification code for the user (important prerequisite)
        req_code_resp = requests.post(request_code_url, json={"email": test_email}, timeout=30)
        assert req_code_resp.status_code == 200
        # The code is stubbed/logged but we won't use it.

        # Attempt to verify email with an invalid code (wrong format)
        invalid_codes = ["000000", "123", "abcdef", "999999"]  # including a correct length code that is presumably invalid/expired

        for code in invalid_codes:
            verify_payload = {
                "email": test_email,
                "code": code
            }
            verify_resp = requests.post(verify_email_url, json=verify_payload, timeout=30)
            assert verify_resp.status_code == 400, \
                f"Expected 400 for invalid/expired/already-used code '{code}', got {verify_resp.status_code}"

    finally:
        # No delete endpoint available for users specified in PRD, so no cleanup possible here
        pass

test_postapiauthverifyemailreturns400forinvalidorexpiredcode()