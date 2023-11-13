﻿using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace burgerBurger.Models
{
    public abstract class OrderItem
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(255)]
        public string? Name { get; set; }

        [Range(0, int.MaxValue)]
        [DisplayName("Total Calories")]
        public int totalCalories { get; set; }

        [Required]
        [Range(0, double.MaxValue)]
        [DisplayFormat(DataFormatString = "{0:c}")]
        public double Price { get; set; }

        public List<Inventory>? Ingredients { get; set; }

        public OrderItem()
        {
            Ingredients = new List<Inventory>();
        }
    }
}