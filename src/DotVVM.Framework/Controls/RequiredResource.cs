using System;
using System.Collections.Generic;
using System.Linq;
using DotVVM.Framework.Binding;
using DotVVM.Framework.Controls.Infrastructure;
using DotVVM.Framework.Hosting;

namespace DotVVM.Framework.Controls
{
    /// <summary>
    /// Declares a resource that will be rendered in the <see cref="BodyResourceLinks" /> control later.
    /// </summary>
    [ControlMarkupOptions(AllowContent = false)]
    public class RequiredResource : DotvvmControl
    {

        /// <summary>
        /// Gets or sets the name of the resource.
        /// </summary>
        [MarkupOptions(Required = true, AllowBinding = false)]
        public string Name
        {
            get { return (string)GetValue(NameProperty); }
            set { SetValue(NameProperty, value); }
        }
        public static readonly DotvvmProperty NameProperty = 
            DotvvmProperty.Register<string, RequiredResource>(c => c.Name);
        

        /// <summary>
        /// Called right before the rendering shall occur.
        /// </summary>
        internal override void OnPreRenderComplete(IDotvvmRequestContext context)
        {
            context.ResourceManager.AddRequiredResource(Name);
            base.OnPreRenderComplete(context);
        }
    }
}
