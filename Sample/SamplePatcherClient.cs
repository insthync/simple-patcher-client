using SimplePatcher;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SamplePatcherClient : MonoBehaviour
{
    public SimplePatcherClient client;

    // Start is called before the first frame update
    void Start()
    {
        client.StartUpdate();
    }

    public void OnClickQuit()
    {
        Application.Quit();
    }
}
