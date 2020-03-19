/*
 * LambdaSharp (λ#)
 * Copyright (C) 2018-2019
 * lambdasharp.net
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using LambdaSharp.Tool.Compiler.Syntax;

namespace LambdaSharp.Tool.Compiler {

    internal static class Fn {

        //--- Class Methods ---
        public static ReferenceFunctionExpression Ref(string referenceName, bool resolved = false) => new ReferenceFunctionExpression {
            ReferenceName = Literal(referenceName),
            Resolved = resolved
        };

        public static GetAttFunctionExpression GetAtt(string referenceName, string attributeName) => new GetAttFunctionExpression {
            ReferenceName = Literal(referenceName),
            AttributeName = Literal(attributeName)
        };

        public static GetAttFunctionExpression GetAtt(string referenceName, AExpression attributeName) => new GetAttFunctionExpression {
            ReferenceName = Literal(referenceName),
            AttributeName = attributeName ?? throw new ArgumentNullException(nameof(attributeName))
        };

        public static SubFunctionExpression Sub(string formatString) => new SubFunctionExpression {
            FormatString = Literal(formatString)
        };

        public static SubFunctionExpression Sub(string formatString, ObjectExpression parameters) => new SubFunctionExpression {
            FormatString = Literal(formatString),
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters))
        };

        public static JoinFunctionExpression Join(string separator, IEnumerable<AExpression> values) => new JoinFunctionExpression {
            Delimiter = Literal(separator),
            Values = new ListExpression(values ?? throw new ArgumentNullException(nameof(values))),
        };

        public static SelectFunctionExpression Select(string index, AExpression values) => new SelectFunctionExpression {
            Index = Literal(index),
            Values = values ?? throw new ArgumentNullException(nameof(values))
        };

        public static SplitFunctionExpression Split(string delimiter, AExpression sourceString) => new SplitFunctionExpression {
            Delimiter = Literal(delimiter),
            SourceString = sourceString ?? throw new ArgumentNullException(nameof(sourceString))
        };

        public static FindInMapFunctionExpression FindInMap(string mapName, AExpression topLevelKey, AExpression secondLevelKey) => new FindInMapFunctionExpression {
            MapName = Literal(mapName),
            TopLevelKey = topLevelKey ?? throw new ArgumentNullException(nameof(topLevelKey)),
            SecondLevelKey = secondLevelKey ?? throw new ArgumentNullException(nameof(secondLevelKey))
        };

        public static ImportValueFunctionExpression ImportValue(AExpression sharedValueToImport) => new ImportValueFunctionExpression {
            SharedValueToImport = sharedValueToImport ?? throw new ArgumentNullException(nameof(sharedValueToImport))
        };

        public static IfFunctionExpression If(string condition, AExpression ifTrue, AExpression ifFalse) => new IfFunctionExpression {
            Condition = Condition(condition),
            IfTrue = ifTrue ?? throw new ArgumentNullException(nameof(ifTrue)),
            IfFalse = ifFalse ?? throw new ArgumentNullException(nameof(ifFalse))
        };

        // TODO: consider inlining to allow SourceLocation to be set more easily
        public static LiteralExpression Literal(string value) => new LiteralExpression(value);

        // TODO: consider inlining to allow SourceLocation to be set more easily
        public static LiteralExpression Literal(int value) => new LiteralExpression(value);

        public static ListExpression LiteralList(params string[] values) => new ListExpression(values.Select(value => Literal(value)));

        public static NotConditionExpression Not(AExpression condition) => new NotConditionExpression {
            Value = condition ?? throw new ArgumentNullException(nameof(condition))
        };

        public static EqualsConditionExpression Equals(AExpression leftValue, AExpression rightValue) => new EqualsConditionExpression {
            LeftValue = leftValue ?? throw new ArgumentNullException(nameof(leftValue)),
            RightValue = rightValue ?? throw new ArgumentNullException(nameof(rightValue))
        };

        public static AndConditionExpression And(AExpression leftValue, AExpression rightValue) => new AndConditionExpression {
            LeftValue = leftValue ?? throw new ArgumentNullException(nameof(leftValue)),
            RightValue = rightValue ?? throw new ArgumentNullException(nameof(rightValue))
        };

        public static OrConditionExpression Or(AExpression leftValue, AExpression rightValue) => new OrConditionExpression {
            LeftValue = leftValue ?? throw new ArgumentNullException(nameof(leftValue)),
            RightValue = rightValue ?? throw new ArgumentNullException(nameof(rightValue))
        };

        public static ConditionExpression Condition(string referenceName) => new ConditionExpression {
            ReferenceName = Literal(referenceName)
        };
    }
}