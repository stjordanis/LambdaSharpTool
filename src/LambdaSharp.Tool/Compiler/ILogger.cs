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

namespace LambdaSharp.Tool.Compiler {

    internal interface ILogger {

        //--- Methods ---
        void Log(IBuildReportEntry entry, SourceLocation? sourceLocation, bool exact);
    }

    internal static class ILoggerEx {

        //--- Methods ---
        public static void Log(this ILogger logger, IBuildReportEntry entry) => logger.Log(entry, sourceLocation: null, exact: true);
        public static void Log(this ILogger logger, IBuildReportEntry entry, SourceLocation sourceLocation) => logger.Log(entry, sourceLocation, exact: true);
        public static void LogInfoVerbose(this ILogger logger, string message, SourceLocation? sourceLocation, bool exact) => logger.Log(new Verbose(message), sourceLocation, exact);
        public static void LogInfoVerbose(this ILogger logger, string message) => logger.LogInfoVerbose(message, sourceLocation: null, exact: true);
        public static void LogInfoPerformance(this ILogger logger, string message, TimeSpan duration, bool? cached, SourceLocation? sourceLocation, bool exact) => logger.Log(new Timing(message, duration, cached), sourceLocation, exact);
        public static void LogInfoPerformance(this ILogger logger, string message, TimeSpan duration, bool? cached) => logger.LogInfoPerformance(message, duration, cached, sourceLocation: null, exact: true);
    }

    public enum BuildReportEntrySeverity {
        Debug,
        Timing,
        Verbose,
        Info,
        Warning,
        Error,
        Fatal
    }

    internal interface IBuildReportEntry {

        //--- Properties ---
        int Code { get; }
        string Message { get; }
        BuildReportEntrySeverity Severity { get; }

        //--- Methods ---
        string Render(SourceLocation? sourceLocation, bool exact) {
            var label = Severity.ToString().ToUpperInvariant();
            if(sourceLocation == null) {
                return $"{label}{Code}: {Message}";
            } else if(exact) {
                return $"{label}{Code}: {Message} @ {sourceLocation.FilePath ?? "n/a"}({sourceLocation.LineNumberStart},{sourceLocation.ColumnNumberStart})";
            } else {
                return $"{label}{Code}: {Message} @ (near) {sourceLocation.FilePath ?? "n/a"}({sourceLocation.LineNumberStart},{sourceLocation.ColumnNumberStart})";
            }
        }
    }

    internal class BuildReportLogger : ILogger {

        //--- Types ---
        public class Entry {

            //--- Fields ---
            private readonly string _rendered;

            //--- Constructors ---
            public Entry(IBuildReportEntry entry, SourceLocation? sourceLocation, bool exact) {
                BuildReportEntry = entry ?? throw new ArgumentNullException(nameof(entry));
                SourceLocation = sourceLocation;
                Exact = exact;
                _rendered = entry.Render(sourceLocation, exact);
            }

            //--- Properties ---
            public IBuildReportEntry BuildReportEntry { get; }
            public SourceLocation? SourceLocation { get; }
            public bool Exact { get; }

            //--- Methods ---
            public override string ToString() => _rendered;
        }

        //--- Fields ---
        private readonly List<Entry> _entries = new List<Entry>();

        //--- Properties ---
        public IEnumerable<string> Messages => _entries.Select(entry => entry.ToString()).ToList();

        //--- Methods ---
        public void Log(IBuildReportEntry buildReportEntry, SourceLocation? sourceLocation, bool exact)
            => _entries.Add(new Entry(buildReportEntry, sourceLocation, exact));
    }
}
