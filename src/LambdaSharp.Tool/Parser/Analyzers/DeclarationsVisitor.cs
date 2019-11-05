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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LambdaSharp.Tool.Internal;
using LambdaSharp.Tool.Parser.Syntax;

namespace LambdaSharp.Tool.Parser.Analyzers {

    public partial class DeclarationsVisitor : ASyntaxVisitor {

        //--- Class Fields ---
        private static readonly HashSet<string> _cloudFormationParameterTypes;
        private static readonly string _decryptSecretFunctionCode;
        private static Regex SecretArnRegex = new Regex(@"^arn:aws:kms:[a-z\-]+-\d:\d{12}:key\/[a-fA-F0-9\-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static Regex SecretAliasRegex = new Regex("^[0-9a-zA-Z/_\\-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        //--- Class Constructor ---
        static DeclarationsVisitor() {

            // load source code for embedded secret decryption function
            _decryptSecretFunctionCode = typeof(DeclarationsVisitor).Assembly.ReadManifestResource("LambdaSharp.Tool.Resources.DecryptSecretFunction.js");

            // create list of natively supported CloudFormation types
            _cloudFormationParameterTypes = new HashSet<string> {
                "String",
                "Number",
                "List<Number>",
                "CommaDelimitedList",
                "AWS::SSM::Parameter::Name",
                "AWS::SSM::Parameter::Value<String>",
                "AWS::SSM::Parameter::Value<List<String>>",
                "AWS::SSM::Parameter::Value<CommaDelimitedList>"
            };
            foreach(var awsType in new[] {
                "AWS::EC2::AvailabilityZone::Name",
                "AWS::EC2::Image::Id",
                "AWS::EC2::Instance::Id",
                "AWS::EC2::KeyPair::KeyName",
                "AWS::EC2::SecurityGroup::GroupName",
                "AWS::EC2::SecurityGroup::Id",
                "AWS::EC2::Subnet::Id",
                "AWS::EC2::Volume::Id",
                "AWS::EC2::VPC::Id",
                "AWS::Route53::HostedZone::Id"
            }) {

                // add vanilla type
                _cloudFormationParameterTypes.Add(awsType);

                // add list of type
                _cloudFormationParameterTypes.Add($"List<{awsType}>");

                // add parameter store reference of type
                _cloudFormationParameterTypes.Add($"AWS::SSM::Parameter::Value<{awsType}>");

                // add parameter store reference of list of type
                _cloudFormationParameterTypes.Add($"AWS::SSM::Parameter::Value<List<{awsType}>>");
            }
        }

        //--- Class Methods ---
        public static bool TryParseModuleFullName(string compositeModuleFullName, out string moduleNamespace, out string moduleName) {
            moduleNamespace = "<BAD>";
            moduleName = "<BAD>";
            if(!ModuleInfo.TryParse(compositeModuleFullName, out var moduleInfo)) {
                return false;
            }
            if((moduleInfo.Version != null) || (moduleInfo.Origin != null)) {
                return false;
            }
            moduleNamespace = moduleInfo.Namespace;
            moduleName = moduleInfo.Name;
            return true;
        }

        private static AValueExpression FnRef(string referenceName) => new ReferenceFunctionExpression {
            ReferenceName = referenceName ?? throw new ArgumentNullException(nameof(referenceName))
        };

        private static AValueExpression FnGetAtt(string referenceName, string attributeName) => new GetAttFunctionExpression {
            ReferenceName = Literal(referenceName),
            AttributeName = Literal(attributeName)
        };

        private static AValueExpression FnSub(string formatString) => new SubFunctionExpression {
            FormatString = Literal(formatString)
        };

        private static AValueExpression FnSplit(string delimiter, AValueExpression sourceString) => new SplitFunctionExpression {
            Delimiter = Literal(delimiter),
            SourceString = sourceString
        };

        private static AValueExpression FnIf(string condition, AValueExpression ifTrue, AValueExpression ifFalse) => new IfFunctionExpression {
            Condition = ConditionLiteral(condition),
            IfTrue = ifTrue ?? throw new ArgumentNullException(nameof(ifTrue)),
            IfFalse = ifFalse ?? throw new ArgumentNullException(nameof(ifFalse))
        };

        private static AConditionExpression FnNot(AConditionExpression condition) => new NotConditionExpression {
            Value = condition ?? throw new ArgumentNullException(nameof(condition))
        };

        // TODO: left/right values should be AValueExpression
        private static AConditionExpression FnEquals(AConditionExpression leftValue, AConditionExpression rightValue) => new EqualsConditionExpression {
            LeftValue = leftValue ?? throw new ArgumentNullException(nameof(leftValue)),
            RightValue = rightValue ?? throw new ArgumentNullException(nameof(rightValue))
        };

        private static AConditionExpression FnAnd(AConditionExpression leftValue, AConditionExpression rightValue) => new AndConditionExpression {
            LeftValue = leftValue ?? throw new ArgumentNullException(nameof(leftValue)),
            RightValue = rightValue ?? throw new ArgumentNullException(nameof(rightValue))
        };

        private static AConditionExpression FnCondition(string referenceName) => new ConditionNameExpression {
            ReferenceName = referenceName ?? throw new ArgumentNullException(nameof(referenceName))
        };

        private static AConditionExpression FnRefCondition(string referenceName) => new ConditionReferenceExpression {
            ReferenceName = referenceName ?? throw new ArgumentNullException(nameof(referenceName))
        };

        private static LiteralExpression Literal(string value) => new LiteralExpression {
            Value = value ?? throw new ArgumentNullException(nameof(value))
        };

        private static LiteralExpression Literal(int value) => new LiteralExpression {
            Value = value.ToString()
        };

        private static ConditionLiteralExpression ConditionLiteral(string value) => new ConditionLiteralExpression {
            Value = value ?? throw new ArgumentNullException(nameof(value))
        };

        //--- Fields ---
        private readonly Builder _builder;

        //--- Constructors ---
        public DeclarationsVisitor(Builder builder) => _builder = builder ?? throw new System.ArgumentNullException(nameof(builder));

        //--- Methods ---
        public override void VisitStart(ASyntaxNode parent, UsingDeclaration node) {

            // check if module reference is valid
            if(!ModuleInfo.TryParse(node.Module.Value, out var moduleInfo)) {
                _builder.LogError($"invalid 'Module' attribute value", node.Module.SourceLocation);
            } else {

                // default to deployment bucket as origin when missing
                if(moduleInfo.Origin == null) {
                    moduleInfo = moduleInfo.WithOrigin(ModuleInfo.MODULE_ORIGIN_PLACEHOLDER);
                }

                // add module reference as a shared dependency
                _builder.AddSharedDependency(node, moduleInfo);
            }
        }

        public override void VisitStart(ASyntaxNode parent, ImportDeclaration node) {

            // validate attributes
            ValidateAllowAttribute(node, node.Type, node.Allow);
        }

        public override void VisitStart(ASyntaxNode parent, VariableDeclaration node) {

            // validate Value attribute
            if(node.Type?.Value == "Secret") {
                if((node.Value is ListExpression) || (node.Value is ObjectExpression)) {
                    _builder.LogError($"variable with type 'Secret' must be a literal value or function expression", node.Value.SourceLocation);
                }
            } else if(node.EncryptionContext != null) {
                _builder.LogError($"variable must have type 'Secret' to use 'EncryptionContext' attribute", node.SourceLocation);
            }
        }

        public override void VisitStart(ASyntaxNode parent, GroupDeclaration node) { }

        public override void VisitStart(ASyntaxNode parent, ResourceDeclaration node) {

            // check if declaration is a resource reference
            if(node.Value != null) {

                // validate attributes
                ValidateAllowAttribute(node, node.Type, node.Allow);

                // referenced resource cannot be conditional
                if(node.If != null) {
                    _builder.LogError($"'If' attribute cannot be used with a referenced resource", node.If.SourceLocation);
                }

                // referenced resource cannot have properties
                if(node.Properties != null) {
                    _builder.LogError($"'Properties' section cannot be used with a referenced resource", node.Properties.SourceLocation);
                }

                // validate Value attribute
                if(node.Value is ListExpression listExpression) {
                    foreach(var arnValue in listExpression.Items) {
                        ValidateARN(arnValue);
                    }
                } else {
                    ValidateARN(node.Value);
                }
            } else {

                // CloudFormation resource must have a type
                if(node.Type == null) {
                    _builder.LogError($"missing 'Type' attribute", node.SourceLocation);
                } else {

                    // the Allow attribute is only valid with native CloudFormation types (not custom resources)
                    if((node.Allow != null) && !IsNativeCloudFormationType(node.Type.Value)) {
                        _builder.LogError($"'Allow' attribute can only be used with AWS resource types", node.Type.SourceLocation);
                    }
                }

                // check if resource is conditional
                if((node.If != null) && !(node.If is ConditionLiteralExpression)) {

                    // creation condition as sub-declaration
                    AddDeclaration(node, new ConditionDeclaration {
                        Condition = new LiteralExpression {
                            SourceLocation = node.If.SourceLocation,
                            Value = "Condition"
                        },
                        Description = null,
                        Value = node.If
                    });
                }
            }

            // local functions
            void ValidateARN(AValueExpression arn) {
                if(
                    !(arn is LiteralExpression literalExpression)
                    || (
                        !literalExpression.Value.StartsWith("arn:", StringComparison.Ordinal)
                        && (literalExpression.Value != "*")
                    )
                ) {
                    _builder.LogError($"'Value' attribute must be a valid ARN or wildcard", arn.SourceLocation);
                }
            }
        }

        public override void VisitStart(ASyntaxNode parent, NestedModuleDeclaration node) {

            // check if module reference is valid
            if(!ModuleInfo.TryParse(node.Module.Value, out var moduleInfo)) {
                _builder.LogError($"invalid 'Module' attribute value", node.Module.SourceLocation);
            } else {

                // default to deployment bucket as origin when missing
                if(moduleInfo.Origin == null) {
                    moduleInfo = moduleInfo.WithOrigin(ModuleInfo.MODULE_ORIGIN_PLACEHOLDER);
                    node.Module.Value = moduleInfo.ToString();
                }

                // add module reference as a shared dependency
                _builder.AddNestedDependency(moduleInfo);

                // NOTE: we cannot validate the parameters and output values from the module until the
                //  nested dependency has been resolved.
            }
        }

        public override void VisitStart(ASyntaxNode parent, PackageDeclaration node) {

            // validate Files attributes
            if(
                !Directory.Exists(node.Files.Value)
                && !Directory.Exists(Path.GetDirectoryName(node.Files.Value))
                && !File.Exists(node.Files.Value)
            ) {
                _builder.LogError($"'Files' attribute must refer to an existing file or folder", node.Files.SourceLocation);
            }
        }

        public override void VisitStart(ASyntaxNode parent, ConditionDeclaration node) { }

        public override void VisitStart(ASyntaxNode parent, MappingDeclaration node) {

            // check if object expression is valid (must have first- and second-level keys)
            if(node.Value.Items.Count > 0) {

                // check that all first-level keys have object expressions
                foreach(var topLevelEntry in node.Value.Items) {
                    if(topLevelEntry.Value is ObjectExpression secondLevelObjectExpression) {
                        if(secondLevelObjectExpression.Items.Count > 0) {
                            _builder.LogError($"missing second-level mappings", secondLevelObjectExpression.SourceLocation);
                        } else {

                            // check that all second-level keys have literal expressions
                            foreach(var secondLevelEntry in secondLevelObjectExpression.Items) {
                                if(!(secondLevelEntry.Value is LiteralExpression)) {
                                    _builder.LogError($"value must be a literal", secondLevelEntry.SourceLocation);
                                }
                            }
                        }
                    } else {
                        _builder.LogError($"value must be an object", topLevelEntry.Value.SourceLocation);
                    }
                }
            } else {
                _builder.LogError($"missing top-level mappings", node.SourceLocation);
            }
        }

        public override void VisitStart(ASyntaxNode parent, ResourceTypeDeclaration node) {
        }

        public override void VisitEnd(ASyntaxNode parent, ResourceTypeDeclaration node) {

            // TODO: better rules for parsing CloudFormation types
            //  - https://docs.aws.amazon.com/AWSCloudFormation/latest/UserGuide/cfn-resource-specification-format.html

            // ensure unique property names
            var names = new HashSet<string>();
            if(node.Properties.Count > 0) {
                foreach(var property in node.Properties) {
                    if(!names.Add(property.Name.Value)) {
                        _builder.LogError("duplicate name", property.Name.SourceLocation);
                    }
                }
            } else {
                _builder.LogError($"empty Properties section", node.SourceLocation);
            }

            // ensure unique attribute names
            names.Clear();
            if(node.Attributes.Count > 0) {
                foreach(var attribute in node.Attributes) {
                    if(!names.Add(attribute.Name.Value)) {
                        _builder.LogError("duplicate name", attribute.Name.SourceLocation);
                    }
                }
            } else {
                _builder.LogError($"empty Attributes section", node.SourceLocation);
            }
        }

        public override void VisitStart(ASyntaxNode parent, ResourceTypeDeclaration.PropertyTypeExpression node) {
            if(!_builder.IsValidCloudFormationName(node.Name.Value)) {
                _builder.LogError($"name must be alphanumeric", node.SourceLocation);
            }
            if(node.Type == null) {

                // default Type is String when omitted
                node.Type = new LiteralExpression {
                    Value = "String"
                };
            } else if(!IsValidCloudFormationType(node.Type.Value)) {
                _builder.LogError($"unsupported type", node.Type.SourceLocation);
            }
        }

        public override void VisitStart(ASyntaxNode parent, ResourceTypeDeclaration.AttributeTypeExpression node) {
            if(!_builder.IsValidCloudFormationName(node.Name.Value)) {
                _builder.LogError($"name must be alphanumeric", node.SourceLocation);
            }
            if(node.Type == null) {

                // default Type is String when omitted
                node.Type = new LiteralExpression {
                    Value = "String"
                };
            } else if(!IsValidCloudFormationType(node.Type.Value)) {
                _builder.LogError($"unsupported type", node.Type.SourceLocation);
            }
        }

        public override void VisitStart(ASyntaxNode parent, MacroDeclaration node) { }

        private void ValidateAllowAttribute(ADeclaration node, LiteralExpression type, TagListDeclaration allow) {
            if(allow != null) {
                if(type == null) {
                    _builder.LogError($"'Allow' attribute requires 'Type' attribute", node.SourceLocation);
                } else if(type?.Value == "AWS") {

                    // nothing to do; any 'Allow' expression is legal
                } else {
                    // TODO: ResourceMapping.IsCloudFormationType(node.Type?.Value), "'Allow' attribute can only be used with AWS resource types"
                }
            }
        }

        private bool IsNativeCloudFormationType(string awsType) {

            // TODO:
            throw new NotImplementedException();
        }

        private void ValidateExpressionIsNumber(ASyntaxNode parent, AValueExpression expression, string errorMessage) {
            if((expression is LiteralExpression literal) && !int.TryParse(literal.Value, out _)) {
                _builder.LogError(errorMessage, expression.SourceLocation);
            }
        }

        private bool IsValidCloudFormationType(string type) {
            switch(type) {

            // CloudFormation primitive types
            case "String":
            case "Long":
            case "Integer":
            case "Double":
            case "Boolean":
            case "Timestamp":
                return true;

            // LambdaSharp primitive types
            case "Secret":
                return true;
            default:
                return false;
            }
        }

        private bool IsValidCloudFormationParameterType(string type) => _cloudFormationParameterTypes.Contains(type);

        // TODO: check AWS type
        private bool IsValidCloudFormationResourceType(string type) => throw new NotImplementedException();

        private T AddDeclaration<T>(AItemDeclaration parent, T declaration) where T : AItemDeclaration {
            parent.AddDeclaration(declaration, new AItemDeclaration.DoNotCallThisDirectly());
            declaration.Visit(parent, new SyntaxHierarchyAnalyzer(_builder));
            return declaration;
        }

        private T AddDeclaration<T>(ModuleDeclaration parent, T declaration) where T : AItemDeclaration {
            parent.Items.Add(declaration);
            declaration.Visit(parent, new SyntaxHierarchyAnalyzer(_builder));
            return declaration;
        }

        private void AddGrant(string name, string awsType, AValueExpression reference, IEnumerable<string> allow, AConditionExpression condition) {

            // TODO: always validate as well
            // ValidateAllowAttribute(node, node.Type, node.Allow);

            // TODO:
            throw new NotImplementedException();
        }

        private bool TryGetLabeledPragma(ModuleDeclaration moduleDeclaration, string key, out AValueExpression value) {
            foreach(var objectPragma in moduleDeclaration.Pragmas.OfType<ObjectExpression>()) {
                if(objectPragma.TryGetValue(key, out value)) {
                    return true;
                }
            }
            value = null;
            return false;
        }

        private bool TryGetOverride(ModuleDeclaration moduleDeclaration, string key, out AValueExpression expression) {
            if(
                TryGetLabeledPragma(moduleDeclaration, "Overrides", out var value)
                && (value is ObjectExpression map)
                && map.TryGetValue(key, out expression)
            ) {
                return true;
            }
            expression = null;
            return false;
        }

        private bool TryGetVariable(ModuleDeclaration moduleDeclaration, string name, out AValueExpression variable, out AConditionExpression condition) {
            if(TryGetOverride(moduleDeclaration, $"Module::{name}", out variable)) {
                condition = null;
                return true;
            }
            if(HasLambdaSharpDependencies(moduleDeclaration)) {
                condition = ConditionLiteral("UseCoreServices");
                variable = FnIf("UseCoreServices", FnRef($"LambdaSharp::{name}"), FnRef("AWS::NoValue"));
                return true;
            }
            variable = null;
            condition = null;
            return false;
        }

        private bool HasPragma(ModuleDeclaration moduleDeclaration, string pragma)
            =>  moduleDeclaration.Pragmas.Items.Any(value => (value is LiteralExpression literalExpression) && (literalExpression.Value == pragma));

        private bool HasPragma(FunctionDeclaration functionDeclaration, string pragma)
            =>  functionDeclaration.Pragmas.Items.Any(value => (value is LiteralExpression literalExpression) && (literalExpression.Value == pragma));

        private bool HasLambdaSharpDependencies(ModuleDeclaration moduleDeclaration) => !HasPragma(moduleDeclaration, "no-lambdasharp-dependencies");
        private bool HasModuleRegistration(ModuleDeclaration moduleDeclaration) => !HasPragma(moduleDeclaration, "no-module-registration");
        private bool HasFunctionRegistration(FunctionDeclaration functionDeclaration) => !HasPragma(functionDeclaration, "no-function-registration");
        private bool HasDeadLetterQueue(FunctionDeclaration functionDeclaration) => !HasPragma(functionDeclaration, "no-dead-letter-queue");
    }
}