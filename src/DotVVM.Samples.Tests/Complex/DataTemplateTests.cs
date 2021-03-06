﻿using Dotvvm.Samples.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Riganti.Utils.Testing.Selenium.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DotVVM.Samples.Tests.Complex
{
    [TestClass]
    public class DataTemplateTests : SeleniumTest
    {
        [TestMethod]
        public void Complex_EmptyDataTemplate_RepeaterGridView()
        {
            RunInAllBrowsers(browser => {
                browser.NavigateToUrl(SamplesRouteUrls.ComplexSamples_EmptyDataTemplate_RepeaterGridView);
                browser.Wait();
                void isDisplayed(string id) => browser.CheckIfIsDisplayed("#" + id);
                void isHidden(string id) => browser.CheckIfIsNotDisplayed("#" + id);
                void isNotPresent(string id) => browser.FindElements("#" + id + " > *").ThrowIfDifferentCountThan(0);

                isHidden("marker1_parent");
                isDisplayed("marker1");

                isNotPresent("marker2_parent");
                isDisplayed("marker2");

                isHidden("marker3_parent");
                isDisplayed("marker3");

                isNotPresent("marker4_parent");
                isDisplayed("marker4");

                isDisplayed("nonempty_marker1_parent");
                isHidden("nonempty_marker1");

                isDisplayed("nonempty_marker2_parent");
                isNotPresent("nonempty_marker2");

                isDisplayed("nonempty_marker3_parent");
                isHidden("nonempty_marker3");

                isDisplayed("nonempty_marker4_parent");
                isNotPresent("nonempty_marker4");

                isHidden("null_marker1_parent");
                isDisplayed("null_marker1");

                isNotPresent("null_marker2_parent");
                isDisplayed("null_marker2");

                isHidden("null_marker3_parent");
                isDisplayed("null_marker3");

                isNotPresent("null_marker4_parent");
                isDisplayed("null_marker4");
            });
        }
    }
}
