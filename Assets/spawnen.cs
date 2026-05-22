using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class spawnen : MonoBehaviour
{
    public Rigidbody2D rb;
    // Start is called before the first frame update
    void Start()
    {
    
    }

    // Update is called once per frame
    void Update()
    {
        rb.linearVelocity = Vector2.up * 100 * Time.deltaTime;
    }
}
