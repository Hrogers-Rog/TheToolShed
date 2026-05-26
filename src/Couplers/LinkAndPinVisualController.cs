using System.Collections.Generic;
using System.Reflection;
using Model;
using UnityEngine;

namespace Toolshed.Couplers
{
    internal sealed class LinkAndPinVisualController : MonoBehaviour
    {
        private readonly List<Renderer> _pocketRenderers = new List<Renderer>();
        private readonly List<Renderer> _pinRenderers = new List<Renderer>();
        private readonly List<Renderer> _linkRenderers = new List<Renderer>();
        private readonly List<GameObject> _pocketObjects = new List<GameObject>();
        private readonly List<GameObject> _pinObjects = new List<GameObject>();
        private readonly List<GameObject> _linkObjects = new List<GameObject>();
        private static readonly FieldInfo OtherEndGearField = typeof(Car.EndGear).GetField("_other", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private Car _car;
        private Car.End _end;
        private bool _partsResolved;

        public void Configure(Car car, Car.End end)
        {
            _car = car;
            _end = end;
            LinkAndPinEndRegistry.Register(car, end, this);
        }

        private void LateUpdate()
        {
            EnsureCar();

            if (!_partsResolved)
            {
                ResolveParts();
            }

            bool pocketVisible = LinkAndPinCustomization.ShowPocket(_car, _end);
            SetRenderers(_pocketRenderers, pocketVisible);
            SetObjects(_pocketObjects, pocketVisible);
            bool pinVisible = LinkAndPinCustomization.ShowPin(_car, _end);
            SetRenderers(_pinRenderers, pinVisible);
            SetObjects(_pinObjects, pinVisible);
            bool linkVisible = ShouldShowLink();
            SetRenderers(_linkRenderers, linkVisible);
            SetObjects(_linkObjects, linkVisible);
        }

        private void ResolveParts()
        {
            _pocketRenderers.Clear();
            _pinRenderers.Clear();
            _linkRenderers.Clear();
            _pocketObjects.Clear();
            _pinObjects.Clear();
            _linkObjects.Clear();

            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Coupler L&P A.003")
                {
                    _pocketObjects.Add(child.gameObject);
                    _pocketRenderers.AddRange(child.GetComponentsInChildren<Renderer>(true));
                }
                else if (child.name == "Pin")
                {
                    _pinObjects.Add(child.gameObject);
                    _pinRenderers.AddRange(child.GetComponentsInChildren<Renderer>(true));
                }
                else if (child.name == "Link")
                {
                    _linkObjects.Add(child.gameObject);
                    _linkRenderers.AddRange(child.GetComponentsInChildren<Renderer>(true));
                }
            }

            _partsResolved = _pocketObjects.Count > 0 || _pinObjects.Count > 0 || _linkObjects.Count > 0 || _pocketRenderers.Count > 0 || _pinRenderers.Count > 0 || _linkRenderers.Count > 0;
            if (_partsResolved)
            {
                bool pocketVisible = LinkAndPinCustomization.ShowPocket(_car, _end);
                SetRenderers(_pocketRenderers, pocketVisible);
                SetObjects(_pocketObjects, pocketVisible);
                bool pinVisible = LinkAndPinCustomization.ShowPin(_car, _end);
                SetRenderers(_pinRenderers, pinVisible);
                SetObjects(_pinObjects, pinVisible);
                bool linkVisible = ShouldShowLink();
                SetRenderers(_linkRenderers, linkVisible);
                SetObjects(_linkObjects, linkVisible);
            }
        }

        private bool ShouldShowLink()
        {
            if (!LinkAndPinCustomization.ShowLooseLink(_car, _end))
            {
                return false;
            }

            Car.EndGear thisGear = LinkAndPinEndRegistry.EndGearFor(_car, _end);
            Car.EndGear otherGear = OtherEndGearField?.GetValue(thisGear) as Car.EndGear;
            if (!LinkAndPinEndRegistry.TryGet(otherGear, out LinkAndPinEndRegistry.Entry otherEntry))
            {
                return true;
            }

            if (!LinkAndPinCustomization.ShowLooseLink(otherEntry.Car, otherEntry.End))
            {
                return true;
            }

            string thisId = _car?.id ?? _car?.GetHashCode().ToString() ?? "";
            string otherId = otherEntry.Car?.id ?? otherEntry.Car?.GetHashCode().ToString() ?? "";
            return string.CompareOrdinal(thisId, otherId) <= 0;
        }

        private void EnsureCar()
        {
            if (_car != null)
            {
                return;
            }

            _car = GetComponentInParent<Car>();
            if (_car != null)
            {
                LinkAndPinEndRegistry.Register(_car, _end, this);
            }
        }

        private static void SetRenderers(List<Renderer> renderers, bool enabled)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = enabled;
                }
            }
        }

        private static void SetObjects(List<GameObject> objects, bool active)
        {
            foreach (GameObject obj in objects)
            {
                if (obj != null && obj.activeSelf != active)
                {
                    obj.SetActive(active);
                }
            }
        }
    }
}
