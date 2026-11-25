using NUnit.Framework;
using UnityEditor;

// The name the player shows: the window title, the Mac application and executable name, and the folder its saves
// and round recordings go to (…/DefaultCompany/REBOOT/). It was the template's "CGD" until 2026-11-25.
public sealed class ProductNameTests
{
    [Test]
    public void TheGameIsCalledReboot()
    {
        Assert.AreEqual("REBOOT", PlayerSettings.productName);
    }
}
