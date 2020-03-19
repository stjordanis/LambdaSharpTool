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

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace LambdaSharp.Tool.Model {

    internal class ModuleNameMappings {

        //--- Constants ---
        public const string CurrentVersion = "2019-07-04";


        //--- Properties ---
        public string Version { get; set; } = CurrentVersion;
        public IDictionary<string, string> ResourceNameMappings { get; set; } = new Dictionary<string, string>();
        public IDictionary<string, string> TypeNameMappings { get; set; } = new Dictionary<string, string>();
    }

    internal class ModuleManifest {

        //--- Constants ---
        public const string CurrentVersion = "2019-07-04";

        //--- Properties ---
        public string Version { get; set; } = CurrentVersion;

        [JsonProperty("Module")]
        public ModuleInfo ModuleInfo { get; set; }
        public string Description { get; set; }
        public string TemplateChecksum { get; set; }
        public DateTime Date { get; set; }
        public VersionInfo CoreServicesVersion { get; set; }
        public IList<ModuleManifestParameterSection> ParameterSections { get; set; } = new List<ModuleManifestParameterSection>();
        public ModuleManifestGitInfo Git { get; set; }
        public IList<string> Artifacts { get; set; } = new List<string>();
        public IList<ModuleManifestDependency> Dependencies { get; set; } = new List<ModuleManifestDependency>();
        public IList<ModuleManifestResourceType> ResourceTypes { get; set; } = new List<ModuleManifestResourceType>();
        public IList<ModuleManifestOutput> Outputs { get; set; } = new List<ModuleManifestOutput>();

        //--- Methods ---
        public string GetModuleTemplatePath() => ModuleInfo.GetArtifactPath($"cloudformation_{ModuleInfo.FullName}_{TemplateChecksum}.json");
        public string GetFullName() => ModuleInfo.FullName;
        public string GetNamespace() => ModuleInfo.Namespace;
        public string GetName() => ModuleInfo.Name;
        public VersionInfo GetVersion() => ModuleInfo.Version;

        public IEnumerable<ModuleManifestParameter> GetAllParameters()
            => ParameterSections.SelectMany(section => section.Parameters);
    }

    internal class ModuleManifestGitInfo {

        //--- Properties ---
        public string Branch { get; set; }
        public string SHA { get; set; }
    }

    internal class ModuleManifestResourceType {

       //--- Properties ---
       public string Type { get; set; }
       public string Description { get; set; }
       public IEnumerable<ModuleManifestResourceProperty> Properties { get; set; } = new List<ModuleManifestResourceProperty>();
       public IEnumerable<ModuleManifestResourceAttribute> Attributes { get; set; } = new List<ModuleManifestResourceAttribute>();
    }

    internal class ModuleManifestResourceProperty {

       //--- Properties ---
       public string Name { get; set; }
       public string Description { get; set; }
       public string Type { get; set; } = "String";
       public bool Required { get; set; } = true;
    }

    internal class ModuleManifestResourceAttribute {

       //--- Properties ---
       public string Name { get; set; }
       public string Description { get; set; }
       public string Type { get; set; } = "String";
    }

    internal class ModuleManifestOutput {

        //--- Properties ---
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
    }

    internal class ModuleManifestMacro {

        //--- Properties ---
        public string Name { get; set; }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ModuleManifestDependencyType {
        Unknown,
        Root,
        Nested,
        Shared
    }

    internal class ModuleManifestDependency {

        //--- Properties ---
        public ModuleInfo ModuleInfo { get; set; }
        public ModuleManifestDependencyType Type { get; set; }
    }

    internal class ModuleManifestParameterSection {

        //--- Properties ---
        public string Title { get; set; }
        public IList<ModuleManifestParameter> Parameters { get; set; } = new List<ModuleManifestParameter>();
    }

    internal class ModuleManifestParameter {

        //--- Properties ---
        public string Name { get; set; }
        public string Type { get; set; }
        public string Label { get; set; }
        public string Default { get; set; }
        public string Import { get; set; }
        public List<string> AllowedValues { get; set; }
        public string AllowedPattern { get; set; }
        public string ConstraintDescription { get; set; }
    }
}