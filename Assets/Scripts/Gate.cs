﻿#define DEBUG_GATE

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using CommonsHelper;

// SEO: after GateManager
public class Gate : MonoBehaviour
{
    /* Sibling components */

    private Collider2D m_Collider2D;
    private SpriteRenderer m_SpriteRenderer;


    /* Parameters */

    [SerializeField, Tooltip("Color of gate and switches that toggle it")]
    private GameColor color = GameColor.Purple;
    public GameColor Color => color;

    private void Awake()
    {
        m_Collider2D = this.GetComponentOrFail<Collider2D>();
        m_SpriteRenderer = this.GetComponentOrFail<SpriteRenderer>();
    }

    private void OnEnable ()
    {
        GateManager.Instance.RegisterGate(this);
    }

    private void OnDisable ()
    {
        GateManager.Instance.UnregisterGate(this);
    }

    public void Toggle()
    {
        m_Collider2D.enabled ^= true;
        m_SpriteRenderer.enabled ^= true;
    }
}