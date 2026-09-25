// The only JavaScript the demo needs: turning a pointer position into a position
// inside the SVG viewBox, and capturing the pointer so a drag keeps working when
// the cursor leaves the drawing surface.
window.clipper2demo = {
  toWorld: (svg, clientX, clientY) => {
    const rect = svg.getBoundingClientRect();
    const view = svg.viewBox.baseVal;
    return {
      x: (clientX - rect.left) / rect.width * view.width + view.x,
      y: (clientY - rect.top) / rect.height * view.height + view.y
    };
  },
  capture: (element, pointerId) => {
    try {
      element.setPointerCapture(pointerId);
    } catch {
      // the pointer may already be gone (a touch that ended, a synthetic event)
    }
  },
  release: (element, pointerId) => {
    try {
      element.releasePointerCapture(pointerId);
    } catch {
      // releasing a pointer that is not captured is not an error here
    }
  }
};
