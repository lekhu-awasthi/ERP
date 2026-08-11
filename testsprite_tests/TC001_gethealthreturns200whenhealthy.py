import requests

def test_get_health_returns_200_when_healthy():
    url = "http://localhost:5155/health"
    try:
        response = requests.get(url, timeout=30)
    except requests.RequestException as e:
        assert False, f"Request to {url} failed with exception: {e}"

    assert response.status_code == 200, f"Expected status code 200 but got {response.status_code}"
    json_data = None
    try:
        json_data = response.json()
    except ValueError:
        assert False, "Response is not valid JSON"

    assert "status" in json_data, "Response JSON does not contain 'status' field"
    assert isinstance(json_data["status"], str), "'status' field is not a string"
    assert json_data["status"].lower() in ("healthy", "ok", "up", "running"), f"Unexpected status value: {json_data['status']}"

test_get_health_returns_200_when_healthy()