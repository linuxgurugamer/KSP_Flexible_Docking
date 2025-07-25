#region license
/*The MIT License (MIT)
FlexoTube - Part module to control flexible docking ports

Copyright (c) 2016 DMagic

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/
#endregion

using KSP.Localization;
using System.Collections;
using UnityEngine;

namespace FlexoTubes
{
    public class FlexoTube : ModuleDockingNode, IContractObjectiveModule
    {
		[KSPField]
		public float maxTranslate;
		[KSPField]
		public float maxRotate;
		[KSPField(isPersistant = true)]
		public int targetYTransFrame;
		[KSPField(isPersistant = true)]
		public int targetXTransFrame;
		[KSPField(isPersistant = true)]
		public int targetYRotFrame;
		[KSPField(isPersistant = true)]
		public int targetXRotFrame;
		[KSPField(isPersistant = true)]
		public bool IsDeployed;
		[KSPField(guiActive = true)]
		public string Status; //  = "Retracted (Basic Mode)"; 
		[KSPField(guiActive = true)]
		public string Magnets; //  = "Enabled";
		[KSPField(guiActive = true)]
		public string MagneticForce = "";
		[KSPField(guiActive = true)]
		public string MagneticTorque = "";
		[KSPField]
		public string ReferenceTransform = "DockBase"; // NO_LOCALIZATION

		[KSPField(isPersistant = true)]
		public bool MagnetsEnabled = true;

		[KSPField]
		public float activeForce = 0.1f;
		[KSPField]
		public float activeTorque = 0.1f;
		[KSPField]
		public float activeRange = 1f;
		[KSPField]
		public float activeReEngage = 1.5f;

		private const int frames = 401;
		private const int startFrame = 200;

		private float frameDist;
		private float frameRot;

        #region  NO_LOCALIZATION
        private const string extendName = "Extend";
		private const string yTranslateName = "TranslateYAxis";
		private const string xTranslateName = "TranslateXAxis";
		private const string rotYName = "RotateYAxis";
		private const string rotXName = "RotateXAxis";
        #endregion

        public int lastXTransFrame;
		public int lastYTransFrame;
		public int lastYRotFrame;
		public int lastXRotFrame;

		private Animation anim;
		private Transform referenceTranslateTransform;
		private Transform referenceRotationTransform;
		private Transform dockBaseTransform;
		private float cachedAcquireForce;
		private float cachedAcquireTorque;
		private float cachedAcquireRange;
		private float cachedReEngage;
		private bool moving;
		private bool resetting;
		private bool engagingDominantPort;

		private ModuleDockingNode cachedOtherNode;
		private float cachedOtherNodeForce = 2;
		private float cachedOtherNodeTorque = 2;
		private float cachedOtherNodeRange = 0.5f;
		private float cachedOtherNodeReEngage = 1;

		public float theta = 0;
		public float dist = 0;

		public bool setRest;

		new public bool CheckContractObjectiveValidity()
		{
			return true;
		}

		new public string GetContractObjectiveType()
		{
			return Localizer.Format("#LOC_FlexDock_1");
		}
		
		public override void OnStart(PartModule.StartState state)
		{
			base.OnStart(state);
            Status = Localizer.Format("#LOC_FlexDock_2");
            Magnets = Localizer.Format("#LOC_FlexDock_3");
            if (state == StartState.Editor)
				return;


            anim = part.FindModelAnimators()[0];
			referenceTranslateTransform = part.FindModelTransform("ReferenceTransform"); // NO_LOCALIZATION
			referenceRotationTransform = part.FindModelTransform("ReferenceRotation"); // NO_LOCALIZATION
            dockBaseTransform = part.FindModelTransform(ReferenceTransform);

			frameDist = (maxTranslate * 2) / (float)((frames - 1) / 2);
			frameRot = (maxRotate * 2) / (float)((frames - 1) / 2);

			cachedAcquireForce = acquireForce;
			cachedAcquireTorque = acquireTorque;
			cachedAcquireRange = acquireRange;
			cachedReEngage = minDistanceToReEngage;

			lastYTransFrame = startFrame;
			lastXTransFrame = startFrame;
			lastYRotFrame = startFrame;
			lastXRotFrame = startFrame;

			Events["ToggleMagnets"].guiName = "Toggle Magnets";

			if (!MagnetsEnabled)
			{
				acquireForce = 0;
				acquireTorque = 0;
				Magnets = Localizer.Format("#LOC_FlexDock_4");
			}
			else
				Magnets = Localizer.Format("#LOC_FlexDock_3");

			if (IsDeployed)
			{
				extensionAnimator(1, 1);
				setRest = true;

				if (MagnetsEnabled)
				{
					acquireForce = activeForce;
					acquireTorque = activeTorque;
				}

				acquireRange = activeRange;
				minDistanceToReEngage = activeReEngage;
				switch (this.state)
				{
					case "PreAttached":
					case "Disabled":
					case "Ready":
					case "Disengage":
					case "Acquire":
					case "Acquire (dockee)":
						lastYTransFrame = startFrame;
						lastXTransFrame = startFrame;
						lastYRotFrame = startFrame;
						lastXRotFrame = startFrame;
						break;
					case "Docked (same vessel)":
					case "Docked (docker)":
					case "Docked (dockee)":
						lastYTransFrame = targetYTransFrame;
						lastXTransFrame = targetXTransFrame;
						lastYRotFrame = targetYRotFrame;
						lastXRotFrame = targetXRotFrame;
						setDockedPosition();
						break;
				}
			}
			else
			{
				if (this.state == Localizer.Format("#LOC_FlexDock_5"))
				{
					Events["Deploy"].active = false;
					Events["Retract"].active = false;
				}
			}
		}

		public override string GetInfo()
		{
			string s = base.GetInfo();

			s += string.Format(Localizer.Format("#LOC_FlexDock_6") +
				"{0:F2}m", maxTranslate);  // NO_LOCALIZATION

            s += string.Format(Localizer.Format("#LOC_FlexDock_7") +
				"{0:F2}°", maxRotate); // NO_LOCALIZATION

            return s;
		}

		public override void OnUpdate()
		{
			Status = setStatus();
			MagneticForce = acquireForce.ToString("N2");
			MagneticTorque = acquireTorque.ToString("N2");

			if (otherNode == null)
			{
				if (cachedOtherNode != null)
				{
					cachedOtherNode.acquireForce = cachedOtherNodeForce;
					cachedOtherNode.acquireTorque = cachedOtherNodeTorque;
					cachedOtherNode.acquireRange = cachedOtherNodeRange;
					cachedOtherNode.minDistanceToReEngage = cachedOtherNodeReEngage;
					cachedOtherNode = null;
				}
			}
			else
			{
				if (cachedOtherNode == null || cachedOtherNode != otherNode)
				{
					cachedOtherNode = otherNode;
					cachedOtherNodeForce = otherNode.acquireForce;
					cachedOtherNodeTorque = otherNode.acquireTorque;
					cachedOtherNodeRange = otherNode.acquireRange;
					cachedOtherNodeReEngage = otherNode.minDistanceToReEngage;

					if (IsDeployed)
					{
						if (MagnetsEnabled)
						{
							otherNode.acquireForce = activeForce;
							otherNode.acquireTorque = activeTorque;
						}
						else
						{
							otherNode.acquireForce = 0;
							otherNode.acquireTorque = 0;
						}

						otherNode.acquireRange = activeRange;
						otherNode.minDistanceToReEngage = activeReEngage;
					}
					else
					{
						if (!MagnetsEnabled)
						{
							otherNode.acquireForce = 0;
							otherNode.acquireTorque = 0;
						}
					}
				}

				if (engagingDominantPort)
				{
					if (otherNode.state == Localizer.Format("#LOC_FlexDock_8"))
					{
						engagingDominantPort = false;
						state = Localizer.Format("#LOC_FlexDock_9");
					}
				}
			}

			if (anim == null)
			{
				moving = false;
				return;
			}

			if (!anim.IsPlaying(extendName))
				moving = false;
		}

		new private void LateUpdate()
		{
			base.LateUpdate();

			if (!IsDeployed)
				return;

			if (moving)
				return;

			if (resetting)
			{
				setRestPosition(2);
				return;
			}

			switch(state)
			{
				case "PreAttached":
				case "Disabled":
					return;
				case "Docked (same vessel)":
				case "Docked (docker)":
				case "Docked (dockee)":
					setDockedPosition();
					break;
				case "Acquire":
					setAcquiringPosition();
					break;
				case "Acquire (dockee)":
					if (otherNode == null)
					{
						if (setRest)
							setRestPosition(1);

						break;
					}
					if (otherNode.GetType() == typeof(FlexoTube))
					{
						if (((FlexoTube)otherNode).IsDeployed)
						{
							if (setRest)
								setRestPosition(1);

							break;
						}
					}

					if (otherNode.state == Localizer.Format("#LOC_FlexDock_10"))
					{
						engagingDominantPort = true;
						state = Localizer.Format("#LOC_FlexDock_10");
						if (setRest)
							setRestPosition(1);
						break;
					}

					setAcquiringPosition();
					break;
				case "Ready":
				case "Disengage":
					if (setRest)
						setRestPosition(1);
					break;
			}

		}

		private string setStatus()
		{
			if (moving)
				return Localizer.Format("#LOC_FlexDock_11");

			switch (state)
			{
				case "PreAttached":
					Events["Deploy"].active = false;
					Events["Retract"].active = false;
					return Localizer.Format("#LOC_FlexDock_12");
				case "Docked (same vessel)":
				case "Docked (docker)":
				case "Docked (dockee)":
					Events["Deploy"].active = false;
					Events["Retract"].active = false;
					return Localizer.Format("#LOC_FlexDock_13");
				case "Acquire":
					return Localizer.Format("#LOC_FlexDock_14");
				case "Acquire (dockee)":
					return Localizer.Format("#LOC_FlexDock_15");
			}

			if (IsDeployed)
			{
				Events["Deploy"].active = false;
				Events["Retract"].active = true;
				return Localizer.Format("#LOC_FlexDock_16");
			}
			else
			{
				Events["Deploy"].active = true;
				Events["Retract"].active = false;
				return Localizer.Format("#LOC_FlexDock_2");
			}
		}

		private void setDockedPosition()
		{
			setTranslation(targetYTransFrame, targetXTransFrame);
			setRotation(targetYRotFrame, targetXRotFrame);
		}

		private void setAcquiringPosition()
		{
			calculateNewTarget();

			int newYTranTarget = getNewFrame(targetYTransFrame, lastYTransFrame, 2);

			int newXTranTarget = getNewFrame(targetXTransFrame, lastXTransFrame, 2);

			setTranslation(newYTranTarget, newXTranTarget);

			int newYRotTarget = getNewFrame(targetYRotFrame, lastYRotFrame, 2);

			int newXRotTarget = getNewFrame(targetXRotFrame, lastXRotFrame, 2);

			setRotation(newYRotTarget, newXRotTarget);
		}

		private void calculateNewTarget()
		{
			if (nodeTransform == null || referenceTranslateTransform == null || otherNode.nodeTransform == null)
			{
				targetYTransFrame = startFrame;
				targetXTransFrame = startFrame;
				return;
			}

			referenceTranslateTransform.rotation = Quaternion.LookRotation(otherNode.nodeTransform.forward * -1f, otherNode.nodeTransform.up);

			Vector3 pos = referenceTranslateTransform.position;
			Vector3 otherPos = otherNode.nodeTransform.position;

			Vector3 line = otherPos - pos;

			Vector3 front = referenceTranslateTransform.forward;

			Vector3 projected = Vector3.Project(line, front);

			Vector3 adjustedProjection = pos + projected;		
			
			Vector3 v1 = otherPos - adjustedProjection;
			Vector3 v2 = dockBaseTransform.up * -1f;
			Vector3 v3 = front;

			theta = Vector3.Angle(v2, v1);

			Vector3 up = Vector3.Cross(v2, v1);
			float sign = Mathf.Sign(Vector3.Dot(v3, up));

			theta *= sign;

			Vector3 flatLine = otherPos - adjustedProjection;

			dist = flatLine.magnitude;

			if (dist >= maxTranslate)
				dist = maxTranslate;

			theta = theta * Mathf.Deg2Rad;

			float XDist = dist * Mathf.Sin(theta) * -1f;
			float YDist = dist * Mathf.Cos(theta) * -1f;

			XDist *= 2;
			YDist *= 2;

			float XShift = XDist / frameDist;
			float YShift = YDist / frameDist;

			targetXTransFrame = startFrame + (int)XShift;
			targetYTransFrame = startFrame + (int)YShift;

			targetYRotFrame = startFrame;
			targetXRotFrame = startFrame;

			calculateNewRotation(front);
		}

		private void calculateNewRotation(Vector3 dir)
		{
			if (referenceRotationTransform == null || referenceTranslateTransform == null)
			{
				targetYTransFrame = startFrame;
				targetXTransFrame = startFrame;
				return;
			}

			Vector3 ori = referenceRotationTransform.position;

			Vector3 newZ = referenceRotationTransform.forward;
			Vector3 newY = referenceRotationTransform.up;
			Vector3 newX = referenceRotationTransform.right;

			float rotXX = Vector3.Dot(newZ, dir);
			float rotXY = Vector3.Dot(newY, dir);

			float rotYX = Vector3.Dot(newZ, dir);
			float rotYY = Vector3.Dot(newX, dir);

			float angX = Mathf.Atan2(rotXY, rotXX);
			float angY = Mathf.Atan2(rotYY, rotYX);

			angX *= Mathf.Rad2Deg;
			angY *= Mathf.Rad2Deg;

			float radius = Mathf.Sqrt(angX * angX + angY * angY);

			if (radius > maxRotate)
			{
				float theta = Mathf.Atan2(angY, angX);
				radius = maxRotate;
				angX = radius * Mathf.Cos(theta);
				angY = radius * Mathf.Sin(theta);
			}

			angX *= -2;
			angY *= -2;

			float XShift = angX / frameRot;
			float YShift = angY / frameRot;

			if (XShift > 200)
				XShift = 200;
			else if (XShift < -200)
				XShift = -200;

			if (YShift > 200)
				YShift = 200;
			else if (YShift < -200)
				YShift = -200;

			targetXRotFrame = startFrame + (int)XShift;
			targetYRotFrame = startFrame + (int)YShift;
		}

		private void setRestPosition(int step)
		{
			int newXTranTarget = getNewFrame(startFrame, lastXTransFrame, step);

			int newYTranTarget = getNewFrame(startFrame, lastYTransFrame, step);

			setTranslation(newYTranTarget, newXTranTarget);

			int newYRotTarget = getNewFrame(startFrame, lastYRotFrame, step);

			int newXRotTarget = getNewFrame(startFrame, lastXRotFrame, step);

			setRotation(newYRotTarget, newXRotTarget);

			targetXRotFrame = startFrame;
			targetXTransFrame = startFrame;
			targetYRotFrame = startFrame;
			targetYTransFrame = startFrame;
		}

		private int getNewFrame(int target, int last, int step)
		{
			int i = last;

			if (target > last)
			{
				i = last + step;
				if (i > target)
					i = target;
			}
			else if (last > target)
			{
				i = last - step;
				if (i < target)
					i = target;
			}

			return i;
		}

		private void setTranslation(int yTrans, int xTrans)
		{

			float yTime = (1f * yTrans) / (1f * frames);
			float xTime = (1f * xTrans) / (1f * frames);

			if (xTime > 0.99f)
				xTime = 0.99f;
			else if (xTime < 0.01f)
				xTime = 0.01f;
			if (yTime > 0.99f)
				yTime = 0.99f;
			else if (yTime < 0.01f)
				yTime = 0.01f;

			anim[xTranslateName].speed = 0;
			anim[xTranslateName].normalizedTime = xTime;
			anim.Blend(xTranslateName, 0.5f);

			anim[yTranslateName].speed = 0;
			anim[yTranslateName].normalizedTime = yTime;
			anim.Blend(yTranslateName, 0.5f);

			lastYTransFrame = yTrans;
			lastXTransFrame = xTrans;
		}

		private void setRotation(int yAxis, int xAxis)
		{
			float yTime = (1f * yAxis) / (1f * frames);
			float xTime = (1f * xAxis) / (1f * frames);

			if (xTime > 0.99f)
				xTime = 0.99f;
			else if (xTime < 0.01f)
				xTime = 0.01f;
			if (yTime > 0.99f)
				yTime = 0.99f;
			else if (yTime < 0.01f)
				yTime = 0.01f;

			anim[rotYName].speed = 0;
			anim[rotYName].normalizedTime = yTime;
			anim.Blend(rotYName, 1);

			anim[rotXName].speed = 0;
			anim[rotXName].normalizedTime = xTime;
			anim.Blend(rotXName, 1);

			lastYRotFrame = yAxis;
			lastXRotFrame = xAxis;
		}

		new public void Decouple()
		{
			base.Decouple();

			if (IsDeployed)
			{
				Events["Deploy"].active = false;
				Events["Retract"].active = true;
			}
			else
			{
				Events["Deploy"].active = true;
				Events["Retract"].active = false;
			}
		}

		new public void Undock()
		{
			base.Undock();

			if (IsDeployed)
			{
				Events["Deploy"].active = false;
				Events["Retract"].active = true;
			}
			else
			{
				Events["Deploy"].active = true;
				Events["Retract"].active = false;
			}
		}

		new public void UndockSameVessel()
		{
			base.UndockSameVessel();

			if (IsDeployed)
			{
				Events["Deploy"].active = false;
				Events["Retract"].active = true;
			}
			else
			{
				Events["Deploy"].active = true;
				Events["Retract"].active = false;
			}
		}

        #region NO_LOCALIZATION
        [KSPEvent(guiActive = true, guiActiveUnfocused = true, externalToEVAOnly = true, active = true)]
		public void Deploy()
		{
			if (resetting)
				StopCoroutine("restThenRetract");

			extensionAnimator(1, 0);

			setRest = true;

			IsDeployed = true;

			if (MagnetsEnabled)
			{
				acquireForce = activeForce;
				acquireTorque = activeTorque;
			}
			else
			{
				acquireForce = 0;
				acquireTorque = 0;
			}

			acquireRange = activeRange;
			minDistanceToReEngage = activeReEngage;

			Events["Deploy"].active = false;
			Events["Retract"].active = true;
		}

		[KSPEvent(guiActive = true, guiActiveUnfocused = true, externalToEVAOnly = true, active = false)]
		public void Retract()
		{
			if (resetting)
				return;

			Events["Deploy"].active = true;
			Events["Retract"].active = false;

			if (this.state == "Acquire" || this.state == "Acquire (dockee)")
				StartCoroutine("restThenRetract");
			else
			{
				setRest = false;

				stopAllAnimators();

				extensionAnimator(-1, 1);

				IsDeployed = false;

				if (MagnetsEnabled)
				{
					acquireForce = cachedAcquireForce;
					acquireTorque = cachedAcquireTorque;
				}
				else
				{
					acquireForce = 0;
					acquireTorque = 0;
				}

				acquireRange = cachedAcquireRange;
				minDistanceToReEngage = cachedReEngage;
			}
		}
        #endregion

        [KSPEvent(guiActive = true, guiActiveUnfocused = true, externalToEVAOnly = true, active = true)]
		public void ToggleMagnets()
		{
			if (MagnetsEnabled)
			{
				MagnetsEnabled = false;
				Magnets = Localizer.Format("#LOC_FlexDock_4");
				acquireForce = 0;
				acquireTorque = 0;

				if (otherNode != null)
				{
					otherNode.acquireForce = 0;
					otherNode.acquireTorque = 0;
				}
			}
			else
			{
				MagnetsEnabled = true;
				Magnets = Localizer.Format("#LOC_FlexDock_3");
				if (IsDeployed)
				{
					acquireForce = activeForce;
					acquireTorque = activeTorque;

					if (otherNode != null)
					{
						otherNode.acquireForce = activeForce;
						otherNode.acquireTorque = activeTorque;
					}
				}
				else
				{
					acquireForce = cachedAcquireForce;
					acquireTorque = cachedAcquireTorque;

					if (otherNode != null)
					{
						otherNode.acquireForce = acquireForce;
						otherNode.acquireTorque = acquireTorque;
					}
				}
			}
		}

		[KSPAction("Deploy Tube")]
		public void DeployAction(KSPActionParam param)
		{
			Deploy();
		}

		[KSPAction("Retract Tube")]
		public void RetractAction(KSPActionParam param)
		{
			Retract();
		}

		[KSPAction("Toggle Tube")]
		public void ToggleAction(KSPActionParam param)
		{
			if (IsDeployed)
				Retract();
			else
				Deploy();
		}

		[KSPAction("Toggle Magnets")]
		public void MagnetsAction(KSPActionParam param)
		{
			ToggleMagnets();
		}

		private void stopAllAnimators()
		{
			anim.Stop(yTranslateName);
			anim.Stop(xTranslateName);
			anim.Stop(rotYName);
			anim.Stop(rotXName);
		}

		private IEnumerator restThenRetract()
		{
			resetting = true;

			while (lastYTransFrame != startFrame || lastXTransFrame != startFrame || lastYRotFrame != startFrame || lastXRotFrame != startFrame)
				yield return null;

			setRest = false;

			stopAllAnimators();

			extensionAnimator(-1, 1);
			IsDeployed = false;

			if (MagnetsEnabled)
			{
				acquireForce = cachedAcquireForce;
				acquireTorque = cachedAcquireTorque;
			}
			else
			{
				acquireForce = 0;
				acquireTorque = 0;
			}

			acquireRange = cachedAcquireRange;
			minDistanceToReEngage = cachedReEngage;

			resetting = false;
		}

		private void extensionAnimator(float speed, float time)
		{
			if (anim == null)
				return;

			if (anim[extendName] == null)
				return;

			anim[extendName].speed = speed;

			if (!anim.IsPlaying(extendName))
			{
				anim[extendName].normalizedTime = time;
				anim[extendName].wrapMode = WrapMode.Once;
				anim.Blend(extendName, 1);
				moving = true;
			}
		}
    }
}
