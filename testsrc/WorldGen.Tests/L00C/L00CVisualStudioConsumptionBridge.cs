#if !NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.VisualStudio.Shell.Interop;

namespace ISRWorldGen.L00C.VisualStudioConsumption
{
    [ComImport]
    [Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOleServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid interfaceId, out IntPtr instance);
    }

    public static class VisualStudioConsumptionBridge
    {
        private const uint VsItemIdRoot = unchecked((uint)-2);

        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);

        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(int reserved, out IBindCtx context);

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(
            [MarshalAs(UnmanagedType.LPWStr)] string commandLine,
            out int argumentCount);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        public static object GetDte(int processId)
        {
            IRunningObjectTable table;
            IBindCtx context;
            Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out table));
            Marshal.ThrowExceptionForHR(CreateBindCtx(0, out context));
            IEnumMoniker enumerator;
            table.EnumRunning(out enumerator);
            IMoniker[] monikers = new IMoniker[1];
            IntPtr fetched = Marshal.AllocCoTaskMem(sizeof(int));
            string expected = "!VisualStudio.DTE.18.0:" + processId;

            try
            {
                while (enumerator.Next(1, monikers, fetched) == 0)
                {
                    try
                    {
                        string name;
                        monikers[0].GetDisplayName(context, null, out name);
                        if (string.Equals(name, expected, StringComparison.Ordinal))
                        {
                            object instance;
                            table.GetObject(monikers[0], out instance);
                            return instance;
                        }
                    }
                    finally
                    {
                        if (monikers[0] != null)
                        {
                            Marshal.ReleaseComObject(monikers[0]);
                            monikers[0] = null;
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(fetched);
                Marshal.ReleaseComObject(enumerator);
                Marshal.ReleaseComObject(context);
                Marshal.ReleaseComObject(table);
            }

            return null;
        }

        public static object GetProperty(object instance, string name)
        {
            return instance.GetType().InvokeMember(
                name,
                BindingFlags.GetProperty,
                null,
                instance,
                null);
        }

        public static object GetItem(object collection, object index)
        {
            return collection.GetType().InvokeMember(
                "Item",
                BindingFlags.InvokeMethod | BindingFlags.GetProperty,
                null,
                collection,
                new[] { index });
        }

        public static object FindProject(object dte, string expectedFullName)
        {
            object solution = GetProperty(dte, "Solution");
            object projects = GetProperty(solution, "Projects");
            int count = Convert.ToInt32(GetProperty(projects, "Count"));
            for (int index = 1; index <= count; index++)
            {
                object match = FindProjectRecursive(GetItem(projects, index), expectedFullName);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        public static object GetSolutionService(object dte)
        {
            Guid solutionId = new Guid("7f7cd0db-91ef-49dc-9fa9-02d128515dd4");
            IntPtr pointer;
            int result = ((IOleServiceProvider)dte).QueryService(ref solutionId, ref solutionId, out pointer);
            Marshal.ThrowExceptionForHR(result);
            if (pointer == IntPtr.Zero)
            {
                throw new InvalidOperationException("Visual Studio returned no SVsSolution service.");
            }

            try
            {
                return Marshal.GetObjectForIUnknown(pointer);
            }
            finally
            {
                Marshal.Release(pointer);
            }
        }

        public static object GetProjectHierarchy(object solutionService, string uniqueName)
        {
            IVsHierarchy hierarchy;
            int result = ((IVsSolution)solutionService).GetProjectOfUniqueName(uniqueName, out hierarchy);
            Marshal.ThrowExceptionForHR(result);
            return hierarchy;
        }

        public static Guid GetProjectGuid(object solutionService, object hierarchy)
        {
            Guid projectGuid;
            int result = ((IVsSolution)solutionService).GetGuidOfProject((IVsHierarchy)hierarchy, out projectGuid);
            Marshal.ThrowExceptionForHR(result);
            return projectGuid;
        }

        public static bool GetSolutionIsDirty(object solutionService)
        {
            object value;
            int result = ((IVsSolution)solutionService).GetProperty(
                (int)__VSPROPID.VSPROPID_IsSolutionDirty,
                out value);
            Marshal.ThrowExceptionForHR(result);
            return Convert.ToBoolean(value);
        }

        public static object GetProjectConfiguration(object hierarchy, string configuration, string platform)
        {
            object provider;
            int propertyResult = ((IVsHierarchy)hierarchy).GetProperty(
                VsItemIdRoot,
                (int)__VSHPROPID.VSHPROPID_ConfigurationProvider,
                out provider);
            Marshal.ThrowExceptionForHR(propertyResult);

            IVsCfg projectConfiguration;
            int configurationResult = ((IVsCfgProvider2)provider).GetCfgOfName(
                configuration,
                platform,
                out projectConfiguration);
            Marshal.ThrowExceptionForHR(configurationResult);
            return projectConfiguration;
        }

        public static VsDebugTargetInfo4[] QueryDebugTargets(object configuration, uint launchFlags)
        {
            Guid queryInterfaceId = new Guid("7666c707-8b4b-461c-b555-348c94cb79df");
            IntPtr pointer;
            int result = ((IVsProjectCfg2)configuration).get_CfgType(ref queryInterfaceId, out pointer);
            Marshal.ThrowExceptionForHR(result);
            if (pointer == IntPtr.Zero)
            {
                throw new InvalidOperationException("Project configuration returned no debug-target query interface.");
            }

            object queryObject;
            try
            {
                queryObject = Marshal.GetObjectForIUnknown(pointer);
            }
            finally
            {
                Marshal.Release(pointer);
            }

            IVsQueryDebuggableProjectCfg2 query = (IVsQueryDebuggableProjectCfg2)queryObject;
            uint[] actual = new uint[1];
            query.QueryDebugTargets(launchFlags, 0, null, actual);
            VsDebugTargetInfo4[] targets = new VsDebugTargetInfo4[actual[0]];
            if (actual[0] != 0)
            {
                query.QueryDebugTargets(launchFlags, actual[0], targets, actual);
            }

            if (actual[0] != targets.Length)
            {
                Array.Resize(ref targets, checked((int)actual[0]));
            }

            return targets;
        }

        public static int ReloadProject(object solutionService, Guid projectGuid)
        {
            return ((IVsSolution4)solutionService).ReloadProject(ref projectGuid);
        }

        public static string[] SplitCommandLine(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return new string[0];
            }

            // CommandLineToArgvW treats the first token specially. Prefix a
            // fixed executable and remove it so argument quoting follows the
            // same Windows rules that the launched process receives.
            int count;
            IntPtr pointer = CommandLineToArgvW("probe.exe " + arguments, out count);
            if (pointer == IntPtr.Zero)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                List<string> values = new List<string>();
                for (int index = 1; index < count; index++)
                {
                    IntPtr item = Marshal.ReadIntPtr(pointer, index * IntPtr.Size);
                    values.Add(Marshal.PtrToStringUni(item));
                }

                return values.ToArray();
            }
            finally
            {
                LocalFree(pointer);
            }
        }

        private static object FindProjectRecursive(object project, string expectedFullName)
        {
            try
            {
                string fullName = Convert.ToString(GetProperty(project, "FullName"));
                if (string.Equals(fullName, expectedFullName, StringComparison.OrdinalIgnoreCase))
                {
                    return project;
                }
            }
            catch (COMException)
            {
            }
            catch (TargetInvocationException)
            {
            }

            try
            {
                object items = GetProperty(project, "ProjectItems");
                int count = Convert.ToInt32(GetProperty(items, "Count"));
                for (int index = 1; index <= count; index++)
                {
                    object item = GetItem(items, index);
                    object subProject = GetProperty(item, "SubProject");
                    if (subProject == null)
                    {
                        continue;
                    }

                    object match = FindProjectRecursive(subProject, expectedFullName);
                    if (match != null)
                    {
                        return match;
                    }
                }
            }
            catch (COMException)
            {
            }
            catch (TargetInvocationException)
            {
            }

            return null;
        }
    }
}
#endif
