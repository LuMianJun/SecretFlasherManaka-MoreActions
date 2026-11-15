// Copyright (c) 2019 hobosore. MIT; see repository THIRD_PARTY_NOTICES.md.
// Fixed upstream Euler addition/subtraction; intentionally not quaternion composition.
namespace SFM.Mmd.Upstream;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

internal static class UnityVMDQuaternionExtentions
{
    public static Quaternion PlusRotation(this Quaternion q1, Quaternion q2)
    {
        return Quaternion.Euler(q1.eulerAngles + q2.eulerAngles);
    }

    public static Quaternion MinusRotation(this Quaternion q1, Quaternion q2)
    {
        return Quaternion.Euler(q1.eulerAngles - q2.eulerAngles);
    }
}
