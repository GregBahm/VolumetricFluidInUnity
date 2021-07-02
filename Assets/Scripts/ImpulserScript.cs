using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ImpulserScript : MonoBehaviour 
{
    public float ForceMultiplier = 100;

    [SerializeField]
    private FluidSimulator simulator;

	void Update ()
    {
        simulator.AddImpulse(transform.localPosition, transform.forward * ForceMultiplier);
	}
}
