﻿using burgerBurger.Data;
using burgerBurger.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Configuration;
using burgerBurger.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.CodeAnalysis;
using Stripe;
using Stripe.Checkout;
using Stripe.Terminal;

namespace burgerBurger.Controllers
{
    public class ShopController : Controller
    {
        // manually add db connection dependency (auto-generated in scaffolded controllers but this is custom)
        private readonly ApplicationDbContext _context;

        // add Configuration dependency so we can read the Stripe API key from appsettings or the Azure Config section
        private readonly IConfiguration _configuration;

        public ShopController(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public IActionResult Index()
        {
            ViewData["types"] = new List<StaticItemType> { StaticItemType.Premade, StaticItemType.Side, StaticItemType.Drink };
            return View();
        }

        public IActionResult Category(StaticItemType itemType)
        {
            if (itemType != null)
            {
                // pass input param val to ViewData for display in the view
                ViewData["itemType"] = itemType;

                // fetch products for display
                var products = _context.StaticItem
                    .Where(p => p.Type == itemType)
                    .Where(i => i.IsSelling)
                    .OrderBy(p => p.Name)
                    .ToList();

                return View(products);
            }

            return RedirectToAction("Index");
        }

        // GET: /products/AddToCart/
        public IActionResult AddToCart(int ItemId, int Quantity, double NewPrice)
        {
            var product = _context.OrderItem.Find(ItemId);
            // check if this cart already contains this product
            var cartItem = _context.CartItems.SingleOrDefault(c => c.ItemId == ItemId && c.CustomerId == GetCustomerId());

            if (cartItem == null)
            {
                // if discount
                if (NewPrice != 0)
                {
                    // create new CartItem and populate the fields
                    cartItem = new CartItem
                    {
                        ItemId = ItemId,
                        Quantity = Quantity,
                        Price = (decimal)NewPrice,
                        CustomerId = GetCustomerId()
                    };
                }
                // if buy 1 get 1 free
                else if (product.discountPercentage == 1)
                {
                    // create new CartItem and populate the fields
                    cartItem = new CartItem
                    {
                        ItemId = ItemId,
                        Quantity = Quantity * 2,
                        Price = (decimal)product.Price,
                        CustomerId = GetCustomerId()
                    };
                }
                // if no dicount
                else
                {
                    // create new CartItem and populate the fields
                    cartItem = new CartItem
                    {
                        ItemId = ItemId,
                        Quantity = Quantity,
                        Price = (decimal)product.Price,
                        CustomerId = GetCustomerId()
                    };
                }               

                _context.Add(cartItem);
            }
            else
            {
                // buy 1 get 1 free
                if (product.discountPercentage == 1)
                {
                    cartItem.Quantity += Quantity * 2;
                    _context.Update(cartItem);
                }               
                else
                {
                    cartItem.Quantity += Quantity;
                    _context.Update(cartItem);
                }
            }

            _context.SaveChanges();

            return RedirectToAction("Cart");
        }

        public IActionResult RemoveFromCart(int id)
        {
            _context.CartItems.Remove(_context.CartItems.Find(id));
            _context.SaveChanges();
            return RedirectToAction("Cart");
        }

        private string GetCustomerId()
        {
            // check if we already have a session var for CustomerId
            if (HttpContext.Session.GetString("CustomerId") == null)
            {
                // create new session var of type string using a Guid
                HttpContext.Session.SetString("CustomerId", Guid.NewGuid().ToString());
            }

            return HttpContext.Session.GetString("CustomerId");
        }

        // GET: /Shop/Cart => display current user's shopping cart
        public IActionResult Cart()
        {
            // identify which cart to display
            var customerId = GetCustomerId();

            // join to parent object so we can also show the Product details
            var cartItems = _context.CartItems
                .Include(c => c.Item)
                .Where(c => c.CustomerId == customerId)
                .ToList();

            // calc cart total for display
            double total = 0;
            foreach (var cartItem in cartItems)
            {
                if(cartItem.Item.discountPercentage == 1)
                {
                    total += (double)cartItem.Price * (cartItem.Quantity / 2);
                }
                else
                {
                    total += (double)cartItem.Price * cartItem.Quantity;
                }
            }
            /*var total = (from c in cartItems
                         select c.Quantity * c.Item.Price).Sum();*/
            ViewData["Total"] = total;

            // calc and store cart quantity total in a session var for display in navbar
            var itemCount = (from c in cartItems
                             select c.Quantity).Sum();
            HttpContext.Session.SetInt32("ItemCount", itemCount);

            return View(cartItems);
        }

        // GET: /Shop/Checkout => show empty checkout page to capture customer info
        [Authorize]
        public IActionResult Checkout()
        {
            var customerId = GetCustomerId();

            var cartItems = _context.CartItems.Where(c => c.CustomerId == customerId).Count();
            if (cartItems == 0)
            {
                return RedirectToAction("Cart");
            }
            var now = TimeOnly.FromDateTime(DateTime.Now);
            var locations = _context.Location.AsEnumerable().Where(l => l.OpeningTime.Value.Ticks < now.Ticks).Where(l => l.ClosingTime.Value.Ticks > now.Ticks || l.ClosingTime.Value.Ticks == 0);
            ViewData["Locations"] = new SelectList(locations, "LocationId", "DisplayName");
            ViewData["balance"] = _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity.Name).Result.balance;
            return View();
        }

        // POST: /Shop/Checkout => create Order object and store as session var before payment
        [HttpPost]
        [Authorize]
        public IActionResult Checkout([Bind("FirstName,LastName,Address,City,Province,PostalCode,Phone,LocationId,DeliveryDate,UsedBalance")] Order order)
        {
            // 7 fields bound from form inputs in method header
            // now auto-fill 3 of the fields we removed from the form
            if (order.DeliveryDate == DateTime.MinValue)
            {
                order.DeliveryDate = DateTime.Now;
                order.Status = "Pending";
            }
            else
            {
                order.Status = "To Be Delivered";
            }

            var location = _context.Location.FindAsync(order.LocationId).Result.ClosingTime.Value.Ticks;
            if (TimeOnly.FromDateTime(DateTime.Now).Ticks > location || location == 0)
            {
                var now = TimeOnly.FromDateTime(DateTime.Now);
                var locations = _context.Location.AsEnumerable().Where(l => l.OpeningTime.Value.Ticks < now.Ticks).Where(l => l.ClosingTime.Value.Ticks > now.Ticks || l.ClosingTime.Value.Ticks == 0);
                ViewData["Locations"] = new SelectList(locations, "LocationId", "DisplayName");
                ViewData["error"] = "This location is currently closed. Please select another.";
                return View(order);
            }
            ViewData["error"] = null;

            order.OrderDate = DateTime.Now;
            order.CustomerId = User.Identity.Name;

            var carts = _context.CartItems
                .Include(c => c.Item)
                .Where(c => c.CustomerId == HttpContext.Session.GetString("CustomerId"))
                .ToList();

            foreach (var cartItem in carts)
            {
                if (cartItem.Item.discountPercentage == 1)
                {
                    order.OrderTotal += (decimal)cartItem.Price * (cartItem.Quantity / 2);
                }
                else
                {
                    order.OrderTotal += (decimal)cartItem.Price * cartItem.Quantity;
                }
            }

            // store the order as session var so we can proceed to payment attempt
            HttpContext.Session.SetObject("Order", order);

            var cartItems = _context.CartItems.Where(c => c.CustomerId == HttpContext.Session.GetString("CustomerId")).ToList();

            // get inventory
            var inventoryItem = _context.Inventory
              .Where(i => i.Location.LocationId == order.LocationId)
              .Where(i => i.itemThrowOutCheck == false)
              .OrderBy(i => i.itemExpirey)
              .ToList();

            bool inventoryCheck = false;
            // loop through each cartitem
            foreach (var cart in cartItems)
            {
                var itemsInv = _context.ItemInventory.Where(i => i.ItemId == cart.ItemId).ToList();
                List<InventoryOutline> inv = new List<InventoryOutline>();
                foreach (var item in itemsInv)
                {
                    inv.Add(_context.InventoryOutline.FirstOrDefault(i => i.InventoryOutlineId == item.IngredientId));
                }
                foreach (var ingredient in inv)
                {
                    inventoryCheck = false;
                    // loop through each inventory item
                    foreach (var inventory in inventoryItem)
                    {
                        // only loop if inventory has not been found
                        if (inventoryCheck == false)
                        {
                            // if ingredients match
                            if (inventory.itemName == ingredient.itemName)
                            {
                                // if there is a enough ingredients available at store
                                if (inventory.quantity >= (1 * cart.Quantity))
                                {
                                    inventory.quantity = inventory.quantity - (1 * cart.Quantity);
                                    inventoryCheck = true;
                                }
                            }
                        }
                    }

                    // if false, ingredient was not found; cancel transaction
                    if (inventoryCheck == false)
                    {
                        return RedirectToAction("Cart", "Shop", new { result = "outOfStock" });
                    }
                }
            }

            HttpContext.Session.SetObject("InventoryUpdates", inventoryItem);

            // redirect to payment
            var user = _context.Users.FirstOrDefault(u => u.UserName == order.CustomerId);
            if (user.balance > order.OrderTotal)
            {
                return RedirectToAction("UseBalance");
            }
            return RedirectToAction("Payment");
        }

        [Authorize]
        public IActionResult UseBalance()
        {
            try
            {
                ViewData["balance"] = _context.Users.FirstOrDefault(u => u.UserName == User.Identity.Name).balance;
                ViewData["total"] = HttpContext.Session.GetObject<Order>("Order").OrderTotal;
                return View();
            }
            catch (NullReferenceException e)
            {
                return RedirectToAction("Cart");
            }
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> UseBalance(string result)
        {
            if (result.Equals("true"))
            {
                var user = _context.Users.FirstOrDefault(u => u.UserName == User.Identity.Name);
                var total = HttpContext.Session.GetObject<Order>("Order").OrderTotal;
                user.balance -= total;
                _context.Update(user);
                _context.SaveChanges();
                var balanceChange = new BalanceAddition { Amount = 0 - total, Balance = user.balance, CustomerId = User.Identity.Name, PaymentDate = DateTime.Now };
                _context.BalanceAdditions.Add(balanceChange);
                _context.SaveChanges();
                return RedirectToAction("SaveOrder");
            }
            return RedirectToAction("Payment");
        }

        // GET: /Shop/Payment => invoke Stripe payment session which displays their payment form
        [Authorize]
        public IActionResult Payment()
        {
            // get the order from the session var
            var order = HttpContext.Session.GetObject<Order>("Order");

            Session session = PaymentMethods.Payment(_configuration, order.OrderTotal, Request.Host.Value, "/Shop/SaveOrder", "/Shop/Cart");

            Response.Headers.Add("Location", session.Url);
            return new StatusCodeResult(303);
        }

        // GET: /Shop/SaveOrder => create Order in DB, add OrderDetails, clear cart
        [Authorize]
        public IActionResult SaveOrder()
        {
            // update inventory stock
            var ings = HttpContext.Session.GetObject<List<Inventory>>("InventoryUpdates");
            foreach (var i in ings)
            {
                var inv = _context.Inventory.Find(i.InventoryId);
                if (inv.quantity != i.quantity)
                {
                    inv.quantity = i.quantity;
                    _context.Inventory.Update(inv);
                }
            }
            _context.SaveChanges();

            // get the order from session var
            var order = HttpContext.Session.GetObject<Order>("Order");

            // save each CartItem as a new OrderDetails record for this order
            var cartItems = _context.CartItems.Where(c => c.CustomerId == HttpContext.Session.GetString("CustomerId")).ToList();

            // fill required PaymentCode temporarily
            order.PaymentCode = HttpContext.Session.GetString("CustomerId");

            // save new order to db
            _context.Add(order);
            _context.SaveChanges();   

            foreach (var item in cartItems)
            {
                var orderDetail = new OrderDetail
                {
                    Quantity = item.Quantity,
                    ItemId = item.ItemId,
                    Price = item.Price,
                    OrderId = order.OrderId
                };
                _context.Add(orderDetail);
            }

            _context.SaveChanges();

            // empty cart
            foreach (var item in cartItems)
            {
                _context.Remove(item);
            }
            _context.SaveChanges();

            // clear session variables
            HttpContext.Session.Clear();

            // redirect to Order Confirmation
            return RedirectToAction("Details", "Orders", new { @id = order.OrderId });
        }
    }
}
