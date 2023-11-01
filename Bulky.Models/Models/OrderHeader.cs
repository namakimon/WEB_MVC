using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bulky.Models.Models
{
    public class OrderHeader
    {
        public int Id { get; set; }
        public string ApplicationUserId { get; set; }
        [ForeignKey(nameof(ApplicationUserId))]
        [ValidateNever]
        public ApplicationUser ApplicationUser { get; set; }
        public DateTime OrderDate { get; set; }
        public DateTime ShippingDate { get; set; }
        public double OrderTotal { get; set; }
        public string? OrderStatus { get; set;}
        public string? PaymentStatus { get; set; }
        public string? TrackingNumber { get; set; }
        public string? Carrier { get; set; }
        public DateTime PaymentDate { get; set; }
        public DateTime PaymenDuetDate { get; set; }
        public string? PaymentIntentId { get; set; }
        public string? SessionId { get; set; }
        [Required]
        [DisplayName("Street Address")]
        public string StreetAddress { get; set; }
        [Required]
        public string City { get; set; }
        [Required]
        public string State { get; set; }
        [DisplayName("Postal code")]
        [Required]
        public string PostalCode { get; set; }
        [DisplayName("Phone number")]
        [Required]
        public string PhoneNumber { get; set; }
        [Required]
        public string Name { get; set; }
    }
}
