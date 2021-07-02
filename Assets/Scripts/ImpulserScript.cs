using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ImpulserScript : MonoBehaviour 
{
    public float ForceMultiplier = 100;

    [SerializeField]
    private FluidSimulator simulator;

    private Vector3 lastPos;


	// Use this for initialization
	void Start ()
    {
        lastPos = transform.localPosition;
	}
	
	// Update is called once per frame
	void Update ()
    {
        simulator.AddImpulse(transform.localPosition, transform.forward * ForceMultiplier);
        //Vector3 movement = (transform.localPosition - lastPos);
        //float distChange = movement.sqrMagnitude;
		//if(distChange > float.Epsilon)
        //{
        //    simulator.AddImpulse(transform.localPosition, movement * ForceMultiplier);
        //}
        //lastPos = transform.localPosition;
	}
}
