import requests
import uuid

BASE_URL = "http://localhost:5155"
TIMEOUT = 30


def test_postapiauthforgotpasswordissuesresetcodeforregisteredemail():
    session = requests.Session()
    # Step 1: Register a new user (EmailUnverified) to ensure a registered email exists
    register_url = f"{BASE_URL}/api/auth/register"
    random_email = f"testuser_{uuid.uuid4().hex[:8]}@example.com"
    register_payload = {
        "fullName": "Test User",
        "email": random_email,
        "phone": "1234567890",
        "password": "Password123!"
    }

    user_id = None
    try:
        reg_resp = session.post(register_url, json=register_payload, timeout=TIMEOUT)
        assert reg_resp.status_code == 201, f"Registration failed: {reg_resp.text}"
        reg_data = reg_resp.json()
        user_id = reg_data.get("userId")
        assert user_id is not None, "No userId returned on registration"
        assert reg_data.get("email") == random_email

        # Step 2: POST /api/auth/forgot-password with the registered email
        forgot_password_url = f"{BASE_URL}/api/auth/forgot-password"
        forgot_payload = {"email": random_email}

        forgot_resp = session.post(forgot_password_url, json=forgot_payload, timeout=TIMEOUT)
        assert forgot_resp.status_code == 200, f"Forgot password request failed: {forgot_resp.text}"
        forgot_data = forgot_resp.json()
        message = forgot_data.get("message")
        # Validate presence of confirmation message, content can vary so just check it's string and non-empty
        assert isinstance(message, str) and len(message) > 0

    finally:
        # Cleanup: Not possible to delete user via provided endpoints; no action here
        pass


test_postapiauthforgotpasswordissuesresetcodeforregisteredemail()
