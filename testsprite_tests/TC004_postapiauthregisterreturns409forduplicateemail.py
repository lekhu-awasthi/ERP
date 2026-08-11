import requests
import uuid

BASE_URL = "http://localhost:5155"
REGISTER_ENDPOINT = "/api/auth/register"
TIMEOUT = 30


def test_postapiauthregisterreturns409forduplicateemail():
    headers = {"Content-Type": "application/json"}
    unique_email = f"testuser+{uuid.uuid4()}@example.com"
    user_data = {
        "fullName": "Test User",
        "email": unique_email,
        "phone": "1234567890",
        "password": "Password123!"
    }

    # First registration attempt - should succeed with 201
    response = requests.post(
        BASE_URL + REGISTER_ENDPOINT, json=user_data, headers=headers, timeout=TIMEOUT
    )
    assert response.status_code == 201, f"Expected 201, got {response.status_code}"
    json_resp = response.json()
    assert "userId" in json_resp and isinstance(json_resp["userId"], str)
    assert json_resp.get("email") == unique_email

    # Second registration attempt with the same email - should fail with 409 Conflict
    response2 = requests.post(
        BASE_URL + REGISTER_ENDPOINT, json=user_data, headers=headers, timeout=TIMEOUT
    )
    assert response2.status_code == 409, f"Expected 409, got {response2.status_code}"


test_postapiauthregisterreturns409forduplicateemail()
