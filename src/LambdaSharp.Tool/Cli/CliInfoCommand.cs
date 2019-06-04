/*
 * MindTouch λ#
 * Copyright (C) 2018-2019 MindTouch, Inc.
 * www.mindtouch.com  oss@mindtouch.com
 *
 * For community documentation and downloads visit mindtouch.com;
 * please review the licensing section.
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LambdaSharp.Tool.Internal;
using McMaster.Extensions.CommandLineUtils;

namespace LambdaSharp.Tool.Cli {

    public class CliInfoCommand : ACliCommand {

        //--- Methods ---
        public void Register(CommandLineApplication app) {
            app.Command("info", cmd => {
                cmd.HelpOption();
                cmd.Description = "Show LambdaSharp information";

                // info options
                var showSensitiveInformationOption = cmd.Option("--show-sensitive", "(optional) Show sensitive information", CommandOptionType.NoValue);
                var tierOption = AddTierOption(cmd);
                var initSettingsCallback = CreateSettingsInitializer(cmd, requireDeploymentTier: false);
                cmd.OnExecute(async () => {
                    Console.WriteLine($"{app.FullName} - {cmd.Description}");
                    var settings = await initSettingsCallback();
                    if(settings == null) {
                        return;
                    }

                    // NOTE (2018-12-11, bjorg): '--tier' is optional for the 'info' command; so we replicate it here without the error reporting
                    settings.Tier = tierOption.Value() ?? Environment.GetEnvironmentVariable("LAMBDASHARP_TIER");
                    await Info(
                        settings,
                        GetGitShaValue(Directory.GetCurrentDirectory(), showWarningOnFailure: false),
                        GetGitBranch(Directory.GetCurrentDirectory(), showWarningOnFailure: false),
                        showSensitiveInformationOption.HasValue()
                    );
                });
            });
        }

        public async Task Info(
            Settings settings,
            string gitSha,
            string gitBranch,
            bool showSensitive
        ) {
            await PopulateToolSettingsAsync(settings, optional: true);
            await PopulateRuntimeSettingsAsync(settings);
            var apiGatewayAccount = await DetermineMissingApiGatewayRolePermissions(settings);

            // show LambdaSharp settings
            Console.WriteLine($"LambdaSharp CLI");
            Console.WriteLine($"    Profile: {settings.ToolProfile ?? "<NOT SET>"}");
            Console.WriteLine($"    Version: {settings.ToolVersion}");
            Console.WriteLine($"    Deployment S3 Bucket: {settings.DeploymentBucketName ?? "<NOT SET>"}");
            Console.WriteLine($"    Deployment Notifications Topic: {ConcealAwsAccountId(settings.DeploymentNotificationsTopic ?? "<NOT SET>")}");
            if(apiGatewayAccount.Arn == null) {
                Console.WriteLine($"    API Gateway Role: <NOT SET>");
            } else if(!apiGatewayAccount.MissingPermissions.Any()) {
                var customRole = apiGatewayAccount.Arn != settings.ApiGatewayAccountRole;
                Console.WriteLine($"    API Gateway Role: {(customRole ? "<CUSTOM> " : "")}{apiGatewayAccount.Arn}");
            } else {
                Console.WriteLine($"    API Gateway Role: <INCOMPLETE>");
                LogWarn($"API Gateway role is incomplete (missing: {string.Join(", ", apiGatewayAccount.MissingPermissions)})");
            }

            // TODO (2019-06-03, bjorg): remove this once we no longer need multiple buckets registered
            if(settings.ModuleBucketNames != null) {
                Console.WriteLine($"    Module S3 Buckets:");
                foreach(var bucketName in settings.ModuleBucketNames) {
                    Console.WriteLine($"        - {bucketName}");
                }
            } else {
                Console.WriteLine($"    Module S3 Buckets: <NOT SET>");
            }
            Console.WriteLine($"LambdaSharp Deployment Tier");
            Console.WriteLine($"    Name: {settings.Tier ?? "<NOT SET>"}");
            Console.WriteLine($"    Version: {settings.TierVersion?.ToString() ?? "<NOT SET>"}");
            Console.WriteLine($"Git");
            Console.WriteLine($"    Branch: {gitBranch ?? "<NOT SET>"}");
            Console.WriteLine($"    SHA: {gitSha ?? "<NOT SET>"}");
            Console.WriteLine($"AWS");
            Console.WriteLine($"    Region: {settings.AwsRegion ?? "<NOT SET>"}");
            Console.WriteLine($"    Account Id: {ConcealAwsAccountId(settings.AwsAccountId ?? "<NOT SET>")}");
            Console.WriteLine($"Tools");
            Console.WriteLine($"    .NET Core CLI Version: {GetDotNetVersion() ?? "<NOT FOUND>"}");
            Console.WriteLine($"    Git CLI Version: {GetGitVersion() ?? "<NOT FOUND>"}");
            Console.WriteLine($"    Amazon.Lambda.Tools: {GetAmazonLambdaToolVersion() ?? "<NOT FOUND>"}");

            // local functions
            string ConcealAwsAccountId(string text) {
                if(showSensitive || (settings.AwsAccountId == null)) {
                    return text;
                }
                return text.Replace(settings.AwsAccountId, new string('*', settings.AwsAccountId.Length));
            }
        }

        private string GetDotNetVersion() {
            var dotNetExe = ProcessLauncher.DotNetExe;
            if(string.IsNullOrEmpty(dotNetExe)) {
                return null;
            }

            // read the dotnet version
            var process = new Process {
                StartInfo = new ProcessStartInfo(dotNetExe, "--version") {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                }
            };
            try {
                process.Start();
                var dotnetVersion = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if(process.ExitCode != 0) {
                    return null;
                }
                return dotnetVersion;
            } catch {
                return null;
            }
        }

        private string GetGitVersion() {

            // constants
            const string GIT_VERSION_PREFIX = "git version ";

            // read the gitSha using 'git' directly
            var process = new Process {
                StartInfo = new ProcessStartInfo("git", "--version") {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                }
            };
            try {
                process.Start();
                var gitVersion = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if(process.ExitCode != 0) {
                    return null;
                }
                if(gitVersion.StartsWith(GIT_VERSION_PREFIX, StringComparison.Ordinal)) {
                    gitVersion = gitVersion.Substring(GIT_VERSION_PREFIX.Length);
                }
                return gitVersion;
            } catch {
                return null;
            }
        }

        private string GetAmazonLambdaToolVersion() {

            // check if dotnet executable can be found
            var dotNetExe = ProcessLauncher.DotNetExe;
            if(string.IsNullOrEmpty(dotNetExe)) {
                return null;
            }

            // check if Amazon Lambda Tools extension is installed
            var result = ProcessLauncher.ExecuteWithOutputCapture(
                dotNetExe,
                new[] { "lambda", "tool", "help" },
                workingFolder: null
            );
            if(result == null) {
                return null;
            }

            // parse version from Amazon Lambda Tools
            var match = Regex.Match(result, @"\((?<Version>.*)\)");
            if(!match.Success) {
                return null;
            }
            return match.Groups["Version"].Value;
        }
    }
}
