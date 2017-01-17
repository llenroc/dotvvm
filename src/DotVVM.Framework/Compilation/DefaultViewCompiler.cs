using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DotVVM.Framework.Compilation.Binding;
using DotVVM.Framework.Compilation.ControlTree;
using DotVVM.Framework.Compilation.ControlTree.Resolved;
using DotVVM.Framework.Compilation.Parser;
using DotVVM.Framework.Compilation.Parser.Dothtml.Parser;
using DotVVM.Framework.Compilation.Parser.Dothtml.Tokenizer;
using DotVVM.Framework.Compilation.Styles;
using DotVVM.Framework.Configuration;
using DotVVM.Framework.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CSharp.RuntimeBinder;
using DotVVM.Framework.Utils;

namespace DotVVM.Framework.Compilation
{
    public class DefaultViewCompiler : IViewCompiler
    {
        public DefaultViewCompiler(DotvvmConfiguration configuration)
        {
            this.configuration = configuration;
            this.controlTreeResolver = configuration.ServiceLocator.GetService<IControlTreeResolver>();
            this.assemblyCache = CompiledAssemblyCache.Instance;
        }


        private readonly CompiledAssemblyCache assemblyCache;
        private readonly IControlTreeResolver controlTreeResolver;
        private readonly DotvvmConfiguration configuration;

        /// <summary>
        /// Compiles the view and returns a function that can be invoked repeatedly. The function builds full control tree and activates the page.
        /// </summary>
        public virtual CSharpCompilation CompileView(string sourceCode, string fileName, CSharpCompilation compilation, string namespaceName, string className)
        {
            // parse the document
            var tokenizer = new DothtmlTokenizer();
            tokenizer.Tokenize(sourceCode);
            var parser = new DothtmlParser();
            var node = parser.Parse(tokenizer.Tokens);

            var resolvedView = (ResolvedTreeRoot)controlTreeResolver.ResolveTree(node, fileName);

            var errorCheckingVisitor = new ErrorCheckingVisitor();
            resolvedView.Accept(errorCheckingVisitor);

            foreach (var token in tokenizer.Tokens)
            {
                if (token.HasError && token.Error.IsCritical)
                {
                    throw new DotvvmCompilationException(token.Error.ErrorMessage, new[] { (token.Error as BeginWithLastTokenOfTypeTokenError<DothtmlToken, DothtmlTokenType>)?.LastToken ?? token });
                }
            }

            foreach (var n in node.EnumerateNodes())
            {
                if (n.HasNodeErrors)
                {
                    throw new DotvvmCompilationException(string.Join(", ", n.NodeErrors), n.Tokens);
                }
            }

            var contextSpaceVisitor = new DataContextIdAssigningVisitor();
            resolvedView.Accept(contextSpaceVisitor);

            var styleVisitor = new StylingVisitor(configuration.Styles);
            resolvedView.Accept(styleVisitor);

            var validationVisitor = new Validation.ControlUsageValidationVisitor(configuration);
            resolvedView.Accept(validationVisitor);
            if (validationVisitor.Errors.Any())
            {
                var controlUsageError = validationVisitor.Errors.First();
                throw new DotvvmCompilationException(controlUsageError.ErrorMessage, controlUsageError.Nodes.SelectMany(n => n.Tokens));
            }

            if (configuration.Debug && configuration.ApplicationPhysicalPath != null)
            {
                var addExpressionDebugvisitor = new ExpressionDebugInfoAddingVisitor(Path.Combine(configuration.ApplicationPhysicalPath, fileName));
                addExpressionDebugvisitor.VisitView(resolvedView);
            }

            var emitter = new DefaultViewCompilerCodeEmitter();
            var compilingVisitor = new ViewCompilingVisitor(emitter, configuration.ServiceLocator.GetService<IBindingCompiler>(), className);

            resolvedView.Accept(compilingVisitor);

            return AddToCompilation(compilation, emitter, fileName, namespaceName, className);
        }

        protected virtual CSharpCompilation AddToCompilation(CSharpCompilation compilation, DefaultViewCompilerCodeEmitter emitter, string fileName, string namespaceName, string className)
        {
            var tree = emitter.BuildTree(namespaceName, className, fileName);
            return compilation
                .AddSyntaxTrees(tree)
                .AddReferences(emitter.UsedAssemblies
                    .Select(a => assemblyCache.GetAssemblyMetadata(a)));
        }

        public virtual CSharpCompilation CreateCompilation(string assemblyName)
        {
            return CSharpCompilation.Create(assemblyName).AddReferences(new[]
                {
                    typeof(RuntimeBinderException).GetTypeInfo().Assembly,
                    typeof(System.Runtime.CompilerServices.DynamicAttribute).GetTypeInfo().Assembly,
                    typeof(DotvvmConfiguration).GetTypeInfo().Assembly,
                    Assembly.Load(new AssemblyName("mscorlib")),
#if DotNetCore
                    Assembly.Load(new AssemblyName("System.Runtime")),
                    Assembly.Load(new AssemblyName("System.Collections.Concurrent")),
                    Assembly.Load(new AssemblyName("System.Collections"))
#else
                    typeof(System.Collections.Generic.List<>).Assembly
#endif
            }.Concat(configuration.Markup.Assemblies.Select(e => Assembly.Load(new AssemblyName(e)))).Distinct()
                .Select(a => assemblyCache.GetAssemblyMetadata(a)))
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        protected virtual IControlBuilder GetControlBuilder(Assembly assembly, string namespaceName, string className)
        {
            return (IControlBuilder)assembly.CreateInstance(namespaceName + "." + className);
        }

        /// <summary>
        /// Builds the assembly.
        /// </summary>
        protected virtual Assembly BuildAssembly(CSharpCompilation compilation)
        {
            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);
                if (result.Success)
                {
                    var assembly = AssemblyLoader.LoadRaw(ms.ToArray());
                    assemblyCache.AddAssembly(assembly, compilation.ToMetadataReference());
                    return assembly;
                }
                else
                {
                    throw new Exception("The compilation failed! This is most probably bug in the DotVVM framework.\r\n\r\n"
                        + string.Join("\r\n", result.Diagnostics)
                        + "\r\n\r\n" + compilation.SyntaxTrees[0].GetRoot().NormalizeWhitespace() + "\r\n\r\n"
                        + "References: " + string.Join("\r\n", compilation.ReferencedAssemblyNames.Select(n => n.Name)));
                }
            }
        }

        public virtual IControlBuilder CompileView(string sourceCode, string fileName, string assemblyName, string namespaceName, string className)
        {
            var compilation = CreateCompilation(assemblyName);
            compilation = CompileView(sourceCode, fileName, compilation, namespaceName, className);
            var assembly = BuildAssembly(compilation);
            return GetControlBuilder(assembly, namespaceName, className);
        }
    }
}