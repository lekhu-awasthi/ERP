import requests
import uuid

BASE_URL = "http://localhost:5155"
TIMEOUT = 30


def test_post_api_auth_request_verification_code_with_registered_email():
    register_url = f"{BASE_URL}/api/auth/register"
    request_verification_code_url = f"{BASE_URL}/api/auth/request-verification-code"

    # Generate unique email for registration to ensure test isolation
    unique_email = f"testuser_{uuid.uuid4()}@example.com"
    registration_payload = {
        "fullName": "Test User",
        "email": unique_email,
        "phone": "1234567890",
        "password": "StrongPassw0rd!"
    }

    session = requests.Session()

    # Register a new user first to ensure the email is registered
    try:
        reg_response = session.post(
            register_url,
            json=registration_payload,
            timeout=TIMEOUT
        )
        assert reg_response.status_code == 201, f"Expected 201 Created, got {reg_response.status_code}"
        reg_resp_json = reg_response.json()
        assert "userId" in reg_resp_json and isinstance(reg_resp_json["userId"], str)
        assert reg_resp_json.get("email") == unique_email

        # Now request the verification code with the registered email
        verification_payload = {
            "email": unique_email
        }
        verification_response = session.post(
            request_verification_code_url,
            json=verification_payload,
            timeout=TIMEOUT
        )
        assert verification_response.status_code == 200, f"Expected 200 OK, got {verification_response.status_code}"
        verification_json = verification_response.json()
        assert "message" in verification_json and isinstance(verification_json["message"], str)
        assert "verification code" in verification_json["message"].lower() or "verification" in verification_json["message"].lower()

    finally:
        # Cleanup is not required because test users are isolated by unique email.
        # If there were an endpoint to delete test user, it should be called here.
        pass


test_post_api_auth_request_verification_code_with_registered_email()