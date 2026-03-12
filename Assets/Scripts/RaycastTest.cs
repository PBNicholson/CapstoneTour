
using UnityEngine;

public class RaycastTest : MonoBehaviour
{
    void Update()
    {
        if (Input.GetMouseButtonDown(1))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            int layerMask = LayerMask.GetMask("TourGeometry");

            if (Physics.Raycast(ray, out RaycastHit hit, 100f, layerMask))
            {
                Debug.Log($"Hit: point={hit.point}, normal={hit.normal}");
                Debug.DrawLine(ray.origin, hit.point, Color.red, 2f);
            }
            else
            {
                Debug.Log("No hit");
            }
        }
    }
}
