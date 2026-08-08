import requests
import uuid

BASE_URL = "http://localhost:5155"
REGISTER_URL = f"{BASE_URL}/api/auth/register"
FORGOT_PASSWORD_URL = f"{BASE_URL}/api/auth/forgot-password"
DELETE_USER_URL_TEMPLATE = f"{BASE_URL}/api/auth/delete/{{user_id}}"  # Assuming delete endpoint for cleanup exists

def test_postapiauthforgotpasswordissuesresetcodeforregisteredemail():
    user_email = f"testuser_{uuid.uuid4()}@example.com"
    user_password = "ValidPass123!"
    user_fullName = "Test User"
    user_phone = "1234567890"

    headers = {"Content-Type": "application/json"}

    # Register new user to ensure email is registered
    register_payload = {
        "fullName": user_fullName,
        "email": user_email,
        "phone": user_phone,
        "password": user_password
    }

    user_id = None

    try:
        register_resp = requests.post(
            REGISTER_URL, json=register_payload, headers=headers, timeout=30
        )
        assert register_resp.status_code == 201, f"Unexpected status at registration: {register_resp.status_code}"
        user_id = register_resp.json().get("userId")
        assert user_id is not None, "userId missing in registration response"
        assert register_resp.json().get("email") == user_email

        # Now test POST /api/auth/forgot-password with registered email
        forgot_password_payload = {"email": user_email}
        forgot_resp = requests.post(
            FORGOT_PASSWORD_URL, json=forgot_password_payload, headers=headers, timeout=30
        )
        assert forgot_resp.status_code == 200, f"Forgot-password returned unexpected status {forgot_resp.status_code}"
        resp_json = forgot_resp.json()
        assert "message" in resp_json and isinstance(resp_json["message"], str) and len(resp_json["message"]) > 0

    finally:
        # Cleanup: If a delete user API exists, delete the registered test user (not defined in PRD, so skipping)
        # If needed, implement user deletion here by user_id to maintain test isolation.
        pass

test_postapiauthforgotpasswordissuesresetcodeforregisteredemail()