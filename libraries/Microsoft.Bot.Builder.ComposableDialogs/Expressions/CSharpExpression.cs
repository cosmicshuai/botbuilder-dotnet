﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Microsoft.Bot.Builder.ComposableDialogs.Expressions
{
    public class CSharpExpression : IExpressionEval
    {
        private static List<MetadataReference> refs = new List<MetadataReference>{
                    MetadataReference.CreateFromFile(typeof(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException).GetTypeInfo().Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.DynamicAttribute).GetTypeInfo().Assembly.Location)};

        private string _expression;
        private Script<object> script;

        public CSharpExpression() { }
        public CSharpExpression(string condition)
        {
            this.Expression = condition;
        }

        public string Expression
        {
            get { return this._expression; }
            set
            {
                this._expression = value;
                this.script = CSharpScript.Create(this._expression, options: ScriptOptions.Default.AddReferences(refs), globalsType: typeof(GlobalState));
            }
        }

        public class GlobalState
        {
            public dynamic State;
            public dynamic Slots;
        }

        public async Task<object> Evaluate(IDictionary<string, object> state)
        {
            if (this.script != null)
            {
                try
                {
                    var result = await script.RunAsync(new GlobalState() { State = state });
                    return result.ReturnValue;
                }
                catch (CompilationErrorException e)
                {
                    Console.WriteLine(string.Join(Environment.NewLine, e.Diagnostics));

                    // TODO WHAT TO THROW?
                    throw;
                }
            }

            throw new ArgumentNullException(nameof(Expression));
        }

    }
}