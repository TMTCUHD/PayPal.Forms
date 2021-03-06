﻿using System;
using Xamarin.PayPal.iOS;
using Foundation;
using System.Diagnostics;
using System.Collections.Generic;
using UIKit;
using System.Linq;

namespace PayPal.Forms.iOS
{
	public class PayPalManager : PayPalPaymentDelegate
	{
		PayPalConfiguration _payPalConfig;

		bool _acceptCreditCards;
		public bool AcceptCreditCards {
			get {
				return _acceptCreditCards;
			}
			set {
				_acceptCreditCards = value;
				_payPalConfig.AcceptCreditCards = _acceptCreditCards;
			}
		}

		string _environment;
		public string Environment {
			get {
				return _environment;
			}
			set {
				if (value != _environment) {
					PayPalMobile.PreconnectWithEnvironment (value);
				}
				_environment = value;
			}
		}

		public PayPalManager(PayPal.Forms.Abstractions.PayPalConfiguration xfconfig)
		{
			NSString key = null;
			NSString value = new NSString (xfconfig.PayPalKey);
			string env = string.Empty;
			switch (xfconfig.Environment) {
			case PayPal.Forms.Abstractions.Enum.Environment.NoNetwork:
				key = Constants.PayPalEnvironmentNoNetwork;
				env = Constants.PayPalEnvironmentNoNetwork.ToString ();
				break;
				case PayPal.Forms.Abstractions.Enum.Environment.Production:
				key = Constants.PayPalEnvironmentProduction;
				env = Constants.PayPalEnvironmentProduction.ToString ();
				break;
				case PayPal.Forms.Abstractions.Enum.Environment.Sandbox:
				key = Constants.PayPalEnvironmentSandbox;
				env = Constants.PayPalEnvironmentSandbox.ToString ();
				break;
			}

			PayPalMobile.InitializeWithClientIdsForEnvironments (NSDictionary.FromObjectsAndKeys (
				new NSObject[] {
					value,
					value,
					value
				}, new NSObject[] {
					key,
					Constants.PayPalEnvironmentProduction,
					Constants.PayPalEnvironmentSandbox
				}
			));

			Environment = env;

			_payPalConfig = new PayPalConfiguration ();
			AcceptCreditCards = xfconfig.AcceptCreditCards;

			// Set up payPalConfig
			_payPalConfig.MerchantName = xfconfig.MerchantName;
			_payPalConfig.MerchantPrivacyPolicyURL = new NSUrl (xfconfig.MerchantPrivacyPolicyUri);
			_payPalConfig.MerchantUserAgreementURL = new NSUrl (xfconfig.MerchantUserAgreementUri);
			_payPalConfig.LanguageOrLocale = NSLocale.PreferredLanguages [0];
			_payPalConfig.PayPalShippingAddressOption = PayPalShippingAddressOption.PayPal;

			Debug.WriteLine ("PayPal iOS SDK Version: " + PayPalMobile.LibraryVersion);
		}

		Action OnCancelled;

		Action<string> OnSuccess;

		Action<string> OnError;

		public void BuyItems(
			PayPal.Forms.Abstractions.PayPalItem[] items,
			Deveel.Math.BigDecimal xfshipping,
			Deveel.Math.BigDecimal xftax,
			Action onCancelled,
			Action<string> onSuccess,
			Action<string> onError
		) {

			OnCancelled = onCancelled;
			OnSuccess = onSuccess;
			OnError = onError;

			List<PayPalItem> nativeItems = new List<PayPalItem> ();
			foreach (var product in items) {
				nativeItems.Add ( PayPalItem.ItemWithName (
					product.Name,
					(nuint)product.Quantity,
					new NSDecimalNumber(DoFormat(product.Price.ToDouble())),
					product.Currency,
					product.SKU)
				);
			}

			var subtotal = PayPalItem.TotalPriceForItems (nativeItems.ToArray ());

			// Optional: include payment details
			var shipping = new NSDecimalNumber(DoFormat(xfshipping.ToDouble()));
			var tax = new NSDecimalNumber (DoFormat (xftax.ToDouble ()));
			var paymentDetails = PayPalPaymentDetails.PaymentDetailsWithSubtotal (subtotal, shipping, tax);

			var total = subtotal.Add (shipping).Add (tax);

			var payment = PayPalPayment.PaymentWithAmount (total, nativeItems.FirstOrDefault().Currency, "Multiple items", PayPalPaymentIntent.Sale);

			payment.Items = nativeItems.ToArray ();
			payment.PaymentDetails = paymentDetails;
			if (payment.Processable) {
				var paymentViewController = new PayPalPaymentViewController(payment, _payPalConfig, this);
				var top = GetTopViewController (UIApplication.SharedApplication.KeyWindow);
				top.PresentViewController (paymentViewController, true, null);
			}else {
				OnError?.Invoke ("This particular payment will always be processable. If, for example, the amount was negative or the shortDescription was empty, this payment wouldn't be processable, and you'd want to handle that here.");
				Debug.WriteLine("Payment not processalbe:"+payment.Items);
			}

		}

		public static string DoFormat( double myNumber )
		{
			var s = string.Format("{0:0.00}", myNumber);

			if ( s.EndsWith("00") )
			{
				return ((int)myNumber).ToString();
			}
			else
			{
				return s;
			}
		}

		public void BuyItem(
			PayPal.Forms.Abstractions.PayPalItem item,
			Deveel.Math.BigDecimal xftax,
			Action onCancelled,
			Action<string> onSuccess,
			Action<string> onError
		){

			OnCancelled = onCancelled;
			OnSuccess = onSuccess;
			OnError = onError;

			NSDecimalNumber amount = new NSDecimalNumber (DoFormat (item.Price.ToDouble ())).Add (new NSDecimalNumber (DoFormat (xftax.ToDouble ())));

			var paymentDetails = PayPalPaymentDetails.PaymentDetailsWithSubtotal (amount, new NSDecimalNumber(0), new NSDecimalNumber (xftax.ToString ()));

			var payment = PayPalPayment.PaymentWithAmount (amount, item.Currency, item.Name, PayPalPaymentIntent.Sale);
			payment.PaymentDetails = paymentDetails;
			payment.Items = new NSObject[]{
				PayPalItem.ItemWithName (
					item.Name,
					1,
					new  NSDecimalNumber (item.Price.ToString ()),
					item.Currency,
					item.SKU
				)
			};
			if (payment.Processable) {
				var paymentViewController = new PayPalPaymentViewController(payment, _payPalConfig, this);
				var top = GetTopViewController (UIApplication.SharedApplication.KeyWindow);
				top.PresentViewController (paymentViewController, true, null);
			}else {
				OnError?.Invoke ("This particular payment will always be processable. If, for example, the amount was negative or the shortDescription was empty, this payment wouldn't be processable, and you'd want to handle that here.");
				OnError = null;
				Debug.WriteLine("Payment not processalbe:"+payment.Items);
			}
		}

		public override void PayPalPaymentDidCancel (PayPalPaymentViewController paymentViewController)
		{
			Debug.WriteLine ("PayPal Payment Cancelled");
			paymentViewController?.DismissViewController(true, null);
			OnCancelled?.Invoke ();
			OnCancelled = null;
		}

		public override void PayPalPaymentViewController (PayPalPaymentViewController paymentViewController, PayPalPayment completedPayment)
		{
			Debug.WriteLine("PayPal Payment Success !");
			paymentViewController.DismissViewController (true, () => {
				Debug.WriteLine ("Here is your proof of payment:" + completedPayment.Confirmation + "Send this to your server for confirmation and fulfillment.");

				NSError err = null;
				NSData jsonData = NSJsonSerialization.Serialize(completedPayment.Confirmation, NSJsonWritingOptions.PrettyPrinted, out err);
				NSString first = new NSString("");
				if(err == null){
					first = new NSString(jsonData, NSStringEncoding.UTF8);
				}else{
					Debug.WriteLine(err.LocalizedDescription);
				}

				OnSuccess?.Invoke (first.ToString());
				OnSuccess =  null;
			});
		}

		UIViewController GetTopViewController(UIWindow window) {
			var vc = window.RootViewController;

			while (vc.PresentedViewController != null) {
				vc = vc.PresentedViewController;
			}

			return vc;
		}
	}
}

