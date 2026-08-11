import requests

BASE_URL = "http://localhost:5155"
TIMEOUT = 30

def test_get_health_returns_service_status():
    url = f"{BASE_URL}/health"
    try:
        response = requests.get(url, timeout=TIMEOUT)
    except requests.RequestException as e:
        assert False, f"Request to {url} failed with exception: {e}"

    assert response.status_code == 200, f"Expected status code 200 but got {response.status_code}"
    try:
        status_text = response.json()
    except ValueError:
        # If response is plain text, fallback to text
        status_text = response.text

    # The specification says response is a status string indicating the service is running.
    assert isinstance(status_text, str), f"Expected response to be a string but got {type(status_text)}"
    assert status_text.strip(), "Expected non-empty status string indicating service status"

test_get_health_returns_service_status()