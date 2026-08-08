import requests
import uuid

BASE_URL = "http://localhost:5155"
TIMEOUT = 30

def test_post_api_auth_request_verification_code_issues_code_for_registered_email():
    register_url = f"{BASE_URL}/api/auth/register"
    request_code_url = f"{BASE_URL}/api/auth/request-verification-code"
    
    # Prepare user data for registration
    unique_email = f"testuser_{uuid.uuid4()}@example.com"
    user_data = {
        "fullName": "Test User",
        "email": unique_email,
        "phone": "1234567890",
        "password": "StrongP@ssword1"
    }
    
    user_id = None
    try:
        # Register a new user (EmailUnverified status)
        register_response = requests.post(register_url, json=user_data, timeout=TIMEOUT)
        assert register_response.status_code == 201, f"Registration failed with status {register_response.status_code}"
        register_json = register_response.json()
        user_id = register_json.get("userId")
        assert user_id is not None, "userId not returned on registration"
        assert register_json.get("email") == unique_email, "Registered email mismatch"
        
        # Request verification code for the registered email
        request_code_payload = {
            "email": unique_email
        }
        code_response = requests.post(request_code_url, json=request_code_payload, timeout=TIMEOUT)
        assert code_response.status_code == 200, f"Request verification code failed with status {code_response.status_code}"
        code_json = code_response.json()
        message = code_json.get("message")
        assert isinstance(message, str) and len(message) > 0, "No confirmation message received"
        
        # No direct access to the code but stub email sender logs it, so test ends here for logged code case
    finally:
        # Cleanup: Delete the created user if registration succeeded
        if user_id:
            delete_url = f"{BASE_URL}/api/auth/users/{user_id}"
            try:
                requests.delete(delete_url, timeout=TIMEOUT)
            except Exception:
                pass

test_post_api_auth_request_verification_code_issues_code_for_registered_email()