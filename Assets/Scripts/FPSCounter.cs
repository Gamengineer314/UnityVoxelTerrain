using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class FPSCounter : MonoBehaviour
{
    public Text text;

    private float time = 0;
    private int framesCount = 0;


    private void Update()
    {
        time += Time.deltaTime;
        framesCount++;
        if (time > 0.5)
        {
            text.text = Mathf.RoundToInt(framesCount / time).ToString();
            time = 0;
            framesCount = 0;
        }
    }
}
