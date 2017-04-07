﻿using System;
using Bit.Core.Utilities;
using Bit.Core.Enums;

namespace Bit.Core.Models.Table
{
    public class Organization : IDataObject<Guid>
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string BusinessName { get; set; }
        public string BillingEmail { get; set; }
        public string Plan { get; set; }
        public PlanType PlanType { get; set; }
        public short? MaxUsers { get; set; }
        public short? MaxSubvaults { get; set; }
        public string StripeCustomerId { get; set; }
        public string StripeSubscriptionId { get; set; }
        public DateTime CreationDate { get; internal set; } = DateTime.UtcNow;
        public DateTime RevisionDate { get; internal set; } = DateTime.UtcNow;

        public void SetNewId()
        {
            Id = CoreHelpers.GenerateComb();
        }
    }
}
