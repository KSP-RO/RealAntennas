﻿#define CIBUILD_disabled
using System.Reflection;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("RealAntennas")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("RealAntennas")]
[assembly: AssemblyCopyright("Copyright ©  2019")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("f3c57338-2b69-47cb-a9ff-7eef1d109490")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion("2.6.0.0")]
#if CIBUILD
[assembly: AssemblyFileVersion("@MAJOR@.@MINOR@.@PATCH@.@BUILD@")]
[assembly: KSPAssembly("RealAntennas", @MAJOR@, @MINOR@)]
#else
[assembly: AssemblyFileVersion("2.99.0.0")]
[assembly: KSPAssembly("RealAntennas", 2, 99)]
#endif

[assembly: KSPAssemblyDependency("ClickThroughBlocker", 1, 8)]
[assembly: KSPAssemblyDependency("KSPCommunityFixes", 1, 36, 0)]
