﻿using System;
using System.Collections.Generic;
using DotVVM.Framework.Binding;
using DotVVM.Framework.Compilation.ControlTree;
using DotVVM.Framework.Compilation.ControlTree.Resolved;
using DotVVM.Framework.Controls;
using DotVVM.Framework.Controls.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DotVVM.Framework.Compilation.Binding;

namespace DotVVM.Framework.Compilation
{
    public class ViewCompilingVisitor : ResolvedControlTreeVisitor
    {
        protected readonly DefaultViewCompilerCodeEmitter emitter;
        protected readonly IBindingCompiler bindingCompiler;

        protected int currentTemplateIndex;
        protected string className;
        protected ControlResolverMetadata lastMetadata;
        protected string controlName;

        public ViewCompilingVisitor(DefaultViewCompilerCodeEmitter emitter, IBindingCompiler bindingCompiler,
            string className)
        {
            this.emitter = emitter;
            this.className = className;
            this.bindingCompiler = bindingCompiler;
        }

        public override void VisitView(ResolvedTreeRoot view)
        {
            lastMetadata = view.Metadata;
            var wrapperClassName = CreateControlClass(className, view.Metadata.Type);
            emitter.UseType(view.Metadata.Type);
            emitter.BuilderDataContextType = view.DataContextTypeStack?.DataContextType;
            emitter.ResultControlType = wrapperClassName;
            // build the statements
            emitter.PushNewMethod(DefaultViewCompilerCodeEmitter.BuildControlFunctionName, typeof(DotvvmControl),
                emitter.EmitControlBuilderParameters());
            var pageName = emitter.EmitCreateObject(wrapperClassName);
            emitter.EmitSetDotvvmProperty(pageName, Internal.UniqueIDProperty, pageName);
            emitter.EmitSetDotvvmProperty(pageName, Internal.MarkupFileNameProperty, view.Metadata.VirtualPath);
            if (typeof(DotvvmView).IsAssignableFrom(view.Metadata.Type))
                emitter.EmitSetProperty(pageName, nameof(DotvvmView.ViewModelType),
                    emitter.EmitValue(view.DataContextTypeStack.DataContextType));
            if (view.Metadata.Type.IsAssignableFrom(typeof(DotvvmView)) ||
                typeof(DotvvmMarkupControl).IsAssignableFrom(view.Metadata.Type))
            {
                foreach (var directive in view.Directives)
                {
                    emitter.EmitAddDirective(pageName, directive.Key, directive.Value.First().Value);
                }
            }

            controlName = pageName;

            base.VisitView(view);

            emitter.EmitReturnClause(pageName);
            emitter.PopMethod();
        }

        protected string EmitCreateControl(Type type, object[] arguments)
        {
            // if matching ctor exists, invoke it directly
            if (type.GetConstructors().Any(ctor =>
                ctor.GetParameters().Length == (arguments?.Length ?? 0) &&
                ctor.GetParameters().Zip(arguments ?? Enumerable.Empty<object>(),
                        (p, a) => TypeConversion.ImplicitConversion(Expression.Constant(a), p.ParameterType))
                    .All(a => a != null)))
                return emitter.EmitCreateObject(type, arguments);
            // othervise invoke DI factory
            else
                return emitter.EmitInjectionFactoryInvocation(
                    type,
                    (arguments ?? Enumerable.Empty<object>()).Select(a => (a.GetType(), emitter.EmitValue(a))).ToArray(),
                    emitter.InvokeDefaultInjectionFactory
                );
        }


        /// <summary>
        /// Processes the node.
        /// </summary>
        public override void VisitControl(ResolvedControl node)
        {
            var parentName = controlName;
            controlName = CreateControl(node);

            base.VisitControl(node);

            emitter.EmitAddCollectionItem(parentName, controlName);
            controlName = parentName;
        }

        private void SetProperty(string controlName, DotvvmProperty property, ExpressionSyntax value)
        {
            emitter.EmitSetDotvvmProperty(controlName, property, value);
        }

        private void SetPropertyValue(string controlName, DotvvmProperty property, object value)
            => SetProperty(controlName, property, emitter.EmitValue(value));

        public override void VisitPropertyValue(ResolvedPropertyValue propertyValue)
        {
            SetPropertyValue(controlName, propertyValue.Property, propertyValue.Value);
            base.VisitPropertyValue(propertyValue);
        }

        public override void VisitPropertyBinding(ResolvedPropertyBinding propertyBinding)
        {
            emitter.EmitSetDotvvmProperty(controlName, propertyBinding.Property, ProcessBinding(propertyBinding.Binding));
            base.VisitPropertyBinding(propertyBinding);
        }

        public override void VisitPropertyControl(ResolvedPropertyControl propertyControl)
        {
            var control = propertyControl.Control;
            var parentName = controlName;
            controlName = CreateControl(control);
            // compile control content
            base.VisitControl(control);
            emitter.EmitSetProperty(controlName, nameof(DotvvmControl.Parent), SyntaxFactory.IdentifierName(parentName));
            // set the property
            SetProperty(parentName, propertyControl.Property, SyntaxFactory.IdentifierName(controlName));
            controlName = parentName;
        }

        public override void VisitPropertyControlCollection(ResolvedPropertyControlCollection propertyControlCollection)
        {
            var parentName = controlName;
            var collectionName = emitter.EmitEnsureCollectionInitialized(parentName, propertyControlCollection.Property);

            foreach (var control in propertyControlCollection.Controls)
            {
                controlName = CreateControl(control);

                // compile control content
                base.VisitControl(control);

                // add to collection in property
                emitter.EmitSetProperty(controlName, nameof(DotvvmControl.Parent), SyntaxFactory.IdentifierName(parentName));
                emitter.EmitAddCollectionItem(collectionName, controlName, null);
            }
            controlName = parentName;
        }

        public override void VisitPropertyTemplate(ResolvedPropertyTemplate propertyTemplate)
        {
            var parentName = controlName;
            var methodName = DefaultViewCompilerCodeEmitter.BuildTemplateFunctionName + $"_{propertyTemplate.Property.DeclaringType.Name}_{propertyTemplate.Property.Name}_{currentTemplateIndex++}";
            emitter.PushNewMethod(methodName, typeof(void), emitter.EmitControlBuilderParameters().Concat(new [] { emitter.EmitParameter("templateContainer", typeof(DotvvmControl))}).ToArray());
            // build the statements
            controlName = "templateContainer";

            base.VisitPropertyTemplate(propertyTemplate);

            emitter.PopMethod();
            controlName = parentName;

            var templateName = CreateTemplate(methodName);
            SetProperty(controlName, propertyTemplate.Property, SyntaxFactory.IdentifierName(templateName));
        }

        /// <summary>
        /// Emits control class definition if wrapper is DotvvmView and returns class name
        /// </summary>
        protected string CreateControlClass(string className, Type wrapperType)
        {
            if (wrapperType == typeof(DotvvmView))
            {
                var controlClassName = className + "Control";
                emitter.EmitControlClass(wrapperType, controlClassName);
                return controlClassName;
            }
            else return wrapperType.FullName;
        }

        /// <summary>
        /// Processes the HTML element that represents a new object.
        /// </summary>
        protected string CreateControl(ResolvedControl control)
        {
            string name;

            if (control.Metadata.ControlBuilderType == null)
            {
                // compiled control
                name = EmitCreateControl(control.Metadata.Type, control.ConstructorParameters);
            }
            else
            {
                // markup control
                name = emitter.EmitInvokeControlBuilder(control.Metadata.Type, control.Metadata.VirtualPath);
            }
            // set unique id
            emitter.EmitSetDotvvmProperty(name, Internal.UniqueIDProperty, name);

            if (control.DothtmlNode != null && control.DothtmlNode.Tokens.Count > 0)
            {
                // set line number
                emitter.EmitSetDotvvmProperty(name, Internal.MarkupLineNumberProperty, control.DothtmlNode.Tokens.First().LineNumber);
            }

            return name;
        }

        /// <summary>
        /// Emits binding contructor and returns variable name
        /// </summary>
        protected ExpressionSyntax ProcessBinding(ResolvedBinding binding)
        {
            return bindingCompiler.EmitCreateBinding(emitter, binding);
        }

        /// <summary>
        /// Processes the template.
        /// </summary>
        protected string CreateTemplate(string builderMethodName)
        {
            var templateName = emitter.EmitCreateObject(typeof(DelegateTemplate));
            emitter.EmitSetProperty(templateName,
                nameof(DelegateTemplate.BuildContentBody),
                emitter.EmitIdentifier(builderMethodName));
            return templateName;
        }
    }
}
