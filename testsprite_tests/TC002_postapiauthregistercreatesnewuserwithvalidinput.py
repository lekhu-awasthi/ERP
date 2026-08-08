import requests
import uuid

base_url = "http://localhost:5155"
register_path = "/api/auth/register"
timeout = 30

def test_postapiauthregistercreatesnewuserwithvalidinput():
    # Prepare unique user data
    unique_email = f"testuser_{uuid.uuid4().hex}@example.com"
    payload = {
        "fullName": "Test User",
        "email": unique_email,
        "phone": "1234567890",
        "password": "StrongPass123"
    }
    headers = {
        "Content-Type": "application/json"
    }

    try:
        response = requests.post(
            url=f"{base_url}{register_path}",
            json=payload,
            headers=headers,
            timeout=timeout
        )
    except requests.RequestException as e:
        assert False, f"Request failed: {e}"

    assert response.status_code == 201, f"Expected status code 201, got {response.status_code}"
    try:
        data = response.json()
    except ValueError:
        assert False, "Response is not valid JSON"

    assert "userId" in data, "Response JSON missing 'userId'"
    assert "email" in data, "Response JSON missing 'email'"
    assert data["email"].lower() == unique_email.lower(), "Returned email does not match the registered email"
    # Optionally validate userId is a valid UUID
    try:
        uuid.UUID(data["userId"])
    except Exception:
        assert False, "userId is not a valid UUID"

test_postapiauthregistercreatesnewuserwithvalidinput()