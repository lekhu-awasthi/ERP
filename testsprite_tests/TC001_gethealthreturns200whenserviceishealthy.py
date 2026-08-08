import requests

BASE_URL = "http://localhost:5155"
TIMEOUT = 30

def test_get_health_returns_200_when_service_is_healthy():
    url = f"{BASE_URL}/health"
    try:
        response = requests.get(url, timeout=TIMEOUT)
        response.raise_for_status()
    except requests.RequestException as e:
        assert False, f"Request to {url} failed: {e}"

    assert response.status_code == 200, f"Expected status code 200, got {response.status_code}"

    try:
        data = response.json()
    except ValueError:
        assert False, "Response is not valid JSON"

    assert "status" in data, "Response JSON does not contain 'status' field"
    assert isinstance(data["status"], str), "'status' field is not a string"
    assert data["status"].lower() in ["healthy", "ok", "up", "running", "available"], \
        f"Unexpected health status value: {data['status']}"

test_get_health_returns_200_when_service_is_healthy()