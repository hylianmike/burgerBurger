﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using burgerBurger.Data;
using burgerBurger.Models;
using Microsoft.AspNetCore.Authorization;

namespace burgerBurger.Controllers
{
    [Authorize]
    public class CustomItemsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public CustomItemsController(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        // GET: CustomItems
        public async Task<IActionResult> Index()
        {
            return _context.CustomItem != null ?
                        View(await _context.CustomItem.ToListAsync()) :
                        Problem("Entity set 'ApplicationDbContext.CustomItem'  is null.");
        }

        // GET: CustomItems/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || _context.CustomItem == null)
            {
                return NotFound();
            }

            var customItem = await _context.CustomItem
                .FirstOrDefaultAsync(m => m.Id == id);
            if (customItem == null)
            {
                return NotFound();
            }

            return View(customItem);
        }

        // GET: CustomItems/Create
        public IActionResult Create()
        {
            ViewData["Breads"] = _context.InventoryOutline.Where(i => i.Category == Enums.InventoryCategory.Bread).ToList();
            ViewData["Meats"] = _context.InventoryOutline.Where(i => i.Category == Enums.InventoryCategory.Meat).ToList();
            ViewData["Toppings"] = _context.InventoryOutline.Where(i => i.Category == Enums.InventoryCategory.Topping).ToList();
            ViewData["Condiments"] = _context.InventoryOutline.Where(i => i.Category == Enums.InventoryCategory.Condiment).ToList(); 
            return View();
        }

        // POST: CustomItems/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string? Ingredients)
        {
            var customBurger = new CustomItem();
            customBurger.Name = "Custom Burger with ";
            customBurger.Price = _configuration.GetValue<double>("CustomBurgerPrice");
            customBurger.Photo = "placeholder.png";

            if (ModelState.IsValid)
            {
                bool breadFlag = false;
                bool meatFlag = false;
                foreach (string i in Ingredients.Split(" "))
                {
                    if (!string.IsNullOrEmpty(i))
                    {
                        int ing = int.Parse(i);
                        var outline = _context.InventoryOutline.Find(ing);

                        if (outline.Category == Enums.InventoryCategory.Bread)
                        {
                            if (!breadFlag) breadFlag = true;
                            else return RedirectToAction("Create", new { result = "error" });
                        }
                        if (outline.Category == Enums.InventoryCategory.Meat)
                        {
                            if (!meatFlag) meatFlag = true;
                            else return RedirectToAction("Create", new { result = "error" });
                        }

                        // increment the total calories of the item by the calories value of each selected ingredient
                        customBurger.totalCalories += outline.calories;
                        customBurger.Name += (outline.itemName + ", ");

                        // add a record of the relationship between the newly made item and the ingredient
                        _context.ItemInventory.Add(new ItemInventory(customBurger.Id, ing));
                    }
                }

                if(!breadFlag || !meatFlag) return RedirectToAction("Create", new { result = "error" });

                customBurger.Name = customBurger.Name.Substring(0, customBurger.Name.Length - 2);
                _context.Add(customBurger);
                await _context.SaveChangesAsync();
                _context.CustomItem.Update(customBurger);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(customBurger);
        }

        // GET: CustomItems/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || _context.CustomItem == null)
            {
                return NotFound();
            }

            var customItem = await _context.CustomItem
                .FirstOrDefaultAsync(m => m.Id == id);
            if (customItem == null)
            {
                return NotFound();
            }

            return View(customItem);
        }

        // POST: CustomItems/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (_context.CustomItem == null)
            {
                return Problem("Entity set 'ApplicationDbContext.CustomItem'  is null.");
            }
            var customItem = await _context.CustomItem.FindAsync(id);
            if (customItem != null)
            {
                _context.CustomItem.Remove(customItem);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool CustomItemExists(int id)
        {
            return (_context.CustomItem?.Any(e => e.Id == id)).GetValueOrDefault();
        }
    }
}
