﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Atomex {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class Resources {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Atomex.Resources", typeof(Resources).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Address not found in local database.
        /// </summary>
        internal static string AddressNotFoundInLocalDb {
            get {
                return ResourceManager.GetString("AddressNotFoundInLocalDb", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to EnumerationValue must be of Enum type.
        /// </summary>
        internal static string EnumerationValueMustBeOfEnumType {
            get {
                return ResourceManager.GetString("EnumerationValueMustBeOfEnumType", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &apos;From&apos; address must not be empty.
        /// </summary>
        internal static string FromAddressIsNullOrEmpty {
            get {
                return ResourceManager.GetString("FromAddressIsNullOrEmpty", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Insufficient funds.
        /// </summary>
        internal static string InsufficientFunds {
            get {
                return ResourceManager.GetString("InsufficientFunds", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Insufficient funds. {0} {1} available.
        /// </summary>
        internal static string InsufficientFundsDetails {
            get {
                return ResourceManager.GetString("InsufficientFundsDetails", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Insufficient funds to cover fees.
        /// </summary>
        internal static string InsufficientFundsToCoverFees {
            get {
                return ResourceManager.GetString("InsufficientFundsToCoverFees", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Insufficient funds to cover fees. {0} {1} required. {2} {1} available.
        /// </summary>
        internal static string InsufficientFundsToCoverFeesDetails {
            get {
                return ResourceManager.GetString("InsufficientFundsToCoverFeesDetails", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Insufficient funds to cover maker network fees.
        /// </summary>
        internal static string InsufficientFundsToCoverMakerNetworkFee {
            get {
                return ResourceManager.GetString("InsufficientFundsToCoverMakerNetworkFee", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Insufficient funds to cover maker network fees. {0} {1} required. {2} {1} available.
        /// </summary>
        internal static string InsufficientFundsToCoverMakerNetworkFeeDetails {
            get {
                return ResourceManager.GetString("InsufficientFundsToCoverMakerNetworkFeeDetails", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Insufficient funds. {0} {1} required. {2} {1} available.
        /// </summary>
        internal static string InsufficientFundsToSendAmountDetails {
            get {
                return ResourceManager.GetString("InsufficientFundsToSendAmountDetails", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Too low fees.
        /// </summary>
        internal static string TooLowFees {
            get {
                return ResourceManager.GetString("TooLowFees", resourceCulture);
            }
        }
    }
}
