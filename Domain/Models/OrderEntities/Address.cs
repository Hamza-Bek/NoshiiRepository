﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.OrderEntities
{
    public class Address
    {
        public string AddressId { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Country { get; set; }
        public string? Street { get; set; }
        public string? Building { get; set; }
        public string? Floor { get; set; }
    }
}
