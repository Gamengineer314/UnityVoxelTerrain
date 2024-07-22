using UnityEngine;

public class CameraFly : MonoBehaviour
{
    private const float xSensitivity = 0.025f;
    private const float ySensitivity = 0.025f;
    private const float speed = 200;

    private float xRotation;
    private float yRotation;


    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }


    private void Update()
    {
        // Translation
        float xMove = 0;
        float zMove = 0;
        if (Input.GetKey(KeyCode.D)) xMove += speed * Time.deltaTime;
        if (Input.GetKey(KeyCode.A)) xMove -= speed * Time.deltaTime;
        if (Input.GetKey(KeyCode.W)) zMove += speed * Time.deltaTime;
        if (Input.GetKey(KeyCode.S)) zMove -= speed * Time.deltaTime;
        transform.Translate(new Vector3(xMove, 0, zMove));

        // Rotation
        xRotation -= Input.GetAxis("Mouse Y") * ySensitivity;
        yRotation += Input.GetAxis("Mouse X") * xSensitivity;
        transform.rotation =
            new Quaternion(0, Mathf.Sin(yRotation / 2), 0, Mathf.Cos(yRotation / 2)) *
            new Quaternion(Mathf.Sin(xRotation / 2), 0, 0, Mathf.Cos(xRotation / 2));
    }
}
