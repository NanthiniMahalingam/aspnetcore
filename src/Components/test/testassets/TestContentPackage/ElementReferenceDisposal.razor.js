export function initialize(element, id) {
  const handler = () => {};
  document.addEventListener('pointermove', handler);
  element.elementReferenceDisposal = { id, handler };
}

export function dispose(element, id) {
  if (element.elementReferenceDisposal.id !== id) {
    throw new Error(`Unexpected disposal state for '${id}'.`);
  }

  document.removeEventListener('pointermove', element.elementReferenceDisposal.handler);
  delete element.elementReferenceDisposal;
  sessionStorage.setItem(`element-reference-disposed-${id}`, 'true');
}
