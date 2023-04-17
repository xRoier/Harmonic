using System;

namespace SharpRtmp.Controllers;

[AttributeUsage(AttributeTargets.Class)]
public class NeverRegisterAttribute : Attribute
{
}